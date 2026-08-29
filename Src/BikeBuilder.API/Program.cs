using BikeBuilder.API.Endpoints;
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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
      options.Authority = builder.Configuration["Auth0:Authority"]
          ?? throw new InvalidOperationException("Auth0:Authority is not configured.");
      options.Audience = builder.Configuration["Auth0:Audience"];
      // False only in the integration-test environment, where the stub OIDC issuer is plain http.
      options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth0:RequireHttpsMetadata", true);
      options.TokenValidationParameters.NameClaimType = "sub";
    });
builder.Services.AddAuthorization();

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

app.MapGrpcService<ComponentGrpcService>().RequireAuthorization();
app.MapGrpcService<BikeBuildGrpcService>().RequireAuthorization();
app.MapComponentImageEndpoints();
// Stays anonymous - the AppHost uses it as the health probe.
app.MapGet("/", () => "BikeBuilder.API gRPC endpoints — use a gRPC-Web client.");
app.MapDefaultEndpoints();

await app.RunAsync();
