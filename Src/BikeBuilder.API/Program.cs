using BikeBuilder.API.Endpoints;
using BikeBuilder.API.UserAdmin;
using Microsoft.AspNetCore.Authentication.JwtBearer;
// Not a global using: OpenTelemetry.Trace's Status/StatusCode collide with Grpc.Core's in
// the gRPC services.
using OpenTelemetry.Trace;

// Azure SDK messaging tracing (Service Bus send/process spans + traceparent stamping on
// messages) is still behind this experimental switch - without it the trace fragments at
// the queue. Must be set before any ServiceBusClient is constructed.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry + health checks + service discovery. "/" is the health probe - not worth a
// trace per poll. SqlClient instrumentation records db.query.text for EF's SQL.
builder.AddServiceDefaults(
    aspNetCoreTraceFilter: context => context.Request.Path != "/",
    configureTracing: tracing => tracing.AddSqlClientInstrumentation());

builder.Services.AddGrpc();

// Connection strings are injected by the AppHost (WithReference); running standalone still
// works with a ConnectionStrings:BikeBuilderDb etc. from any config source.
builder.AddSqlServerDbContext<BikeBuilderDbContext>("BikeBuilderDb");
builder.AddAzureBlobContainerClient("component-images");
builder.AddAzureServiceBusClient("servicebus");
builder.Services.AddSingleton(sp => sp.GetRequiredService<ServiceBusClient>().CreateSender(ServiceBusQueueNames.Notifications));
builder.Services.AddSingleton<ComponentImageStorageService>();
builder.Services.AddSingleton<IEventPublisher, ServiceBusEventPublisher>();

var webAppOrigins = builder.Configuration.GetSection("WebAppOrigins").Get<string[]>()
    ?? ["https://localhost:7200", "http://localhost:7201"];

builder.Services.AddCors(options =>
{
  options.AddPolicy("BlazorWasmClient", policy =>
      policy.WithOrigins(webAppOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding"));
});

// "role" in test mode (the stub issuer's plain claim), the Auth0 namespaced claim otherwise.
var roleClaim = RoleClaim.Resolve(builder.Configuration[RoleClaim.ConfigKey]);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
      options.Authority = builder.Configuration["Auth0:Authority"]
          ?? throw new InvalidOperationException("Auth0:Authority is not configured.");
      options.Audience = builder.Configuration["Auth0:Audience"];
      // False only in the integration-test environment, where the stub OIDC issuer is plain http.
      options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth0:RequireHttpsMetadata", true);
      // Keep the token's claim types as issued: the legacy inbound map renames "sub" and
      // "role" to SOAP-era URIs, which would break both claim type settings below.
      options.MapInboundClaims = false;
      options.TokenValidationParameters.NameClaimType = "sub";
      options.TokenValidationParameters.RoleClaimType = roleClaim;
    });
// The same policy set the WASM app registers client-side - see AuthorizationConstants.
builder.Services.AddAuthorization(options =>
{
  foreach (var (name, allowedRoles) in Policies.All)
    options.AddPolicy(name, policy => policy.RequireRole(allowedRoles));
});

// The Admin section's user store: the test OIDC mock in integration tests, the Auth0
// Management API when its M2M credentials are configured (user secrets), a stub otherwise
// so /admin degrades to a "not configured" notice.
// Singletons: the mock directory carries the in-memory user registry and the token provider
// carries the cached management token - per-request instances would lose both.
if (builder.Configuration["UserAdmin:MockUrl"] is { Length: > 0 })
{
  builder.Services.AddHttpClient();
  builder.Services.AddSingleton<IUserDirectory>(sp => new OidcMockUserDirectory(
      sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
      sp.GetRequiredService<IConfiguration>()));
}
else if (builder.Configuration["Auth0:Management:Domain"] is { Length: > 0 } managementDomain)
{
  builder.Services.AddHttpClient();
  // The directory client keeps the shared default (no retry on its POST/DELETE - a replayed
  // user creation would fail with a duplicate). The token fetch is a POST too, but repeating
  // it is harmless, so its client gets the full retry policy back.
  // EXTEXP0001: RemoveAllResilienceHandlers is still marked experimental, but it's the
  // documented way to swap the ConfigureHttpClientDefaults handler for a per-client one.
#pragma warning disable EXTEXP0001
  builder.Services.AddHttpClient("auth0-token")
      .RemoveAllResilienceHandlers()
      .AddStandardResilienceHandler();
#pragma warning restore EXTEXP0001
  builder.Services.AddSingleton(sp => new Auth0ManagementTokenProvider(
      sp.GetRequiredService<IHttpClientFactory>().CreateClient("auth0-token"),
      sp.GetRequiredService<IConfiguration>()));
  builder.Services.AddSingleton<IUserDirectory>(sp =>
  {
    var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    client.BaseAddress = new Uri($"https://{managementDomain}/api/v2/");
    return new Auth0ManagementUserDirectory(client, sp.GetRequiredService<Auth0ManagementTokenProvider>());
  });
}
else
{
  builder.Services.AddSingleton<IUserDirectory, NullUserDirectory>();
}

var app = builder.Build();

// Local dev/test only: apply EF migrations at startup so the AppHost's freshly provisioned
// SQL container is usable immediately (production would run migrations as a deploy step).
if (app.Environment.IsDevelopment())
{
  using var scope = app.Services.CreateScope();
  await scope.ServiceProvider.GetRequiredService<BikeBuilderDbContext>().Database.MigrateAsync();
}

app.UseCors("BlazorWasmClient");
// gRPC-Web unwrapping must happen before authentication reads the request.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.UseAuthentication();
app.UseAuthorization();

// Auth moved to attributes on the service classes: [Authorize] with [AllowAnonymous] on the
// catalog READ methods, so the public storefront (Web.Public) and the Orders service can
// browse and price-snapshot without a token. Writes stay JWT-authenticated.
app.MapGrpcService<ComponentGrpcService>();
app.MapGrpcService<BikeBuildGrpcService>();
app.MapComponentImageEndpoints();
app.MapAdminUserEndpoints();
// Stays anonymous - the AppHost uses it as the health probe.
app.MapGet("/", () => "BikeBuilder.API gRPC endpoints — use a gRPC-Web client.");
app.MapDefaultEndpoints();

await app.RunAsync();
