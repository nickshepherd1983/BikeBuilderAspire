using BikeBuilder.API.Orders.GraphQL;
using BikeBuilder.API.Protos;
using Grpc.Net.Client.Web;
using HotChocolate.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using OpenTelemetry.Trace;

// Azure SDK messaging tracing (Service Bus send spans + traceparent stamping on the
// OrderPlaced events) is still behind this experimental switch. Must be set before any
// ServiceBusClient is constructed.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(
    aspNetCoreTraceFilter: context => context.Request.Path != "/",
    configureTracing: tracing => tracing.AddSqlClientInstrumentation());

// HotChocolate resolvers can run in parallel, so this deviates from the other apps'
// AddSqlServerDbContext: register a DbContext FACTORY ourselves, then let Aspire enrich it
// (telemetry, health check, retries). The null-guard keeps `dotnet ef migrations add`
// working with no connection string injected. Not pooled - Aspire's enrichment needs the
// scoped DbContext registration AddDbContextFactory also provides.
var ordersConnectionString = builder.Configuration.GetConnectionString("BikeBuilderOrdersDb");
builder.Services.AddDbContextFactory<OrdersDbContext>(options =>
    _ = ordersConnectionString is null ? options.UseSqlServer() : options.UseSqlServer(ordersConnectionString));
if (ordersConnectionString is not null)
  builder.EnrichSqlServerDbContext<OrdersDbContext>();

builder.AddAzureServiceBusClient("servicebus");
builder.Services.AddSingleton(sp => sp.GetRequiredService<ServiceBusClient>().CreateSender(ServiceBusQueueNames.Notifications));
builder.Services.AddSingleton<IEventPublisher, ServiceBusEventPublisher>();

// Catalog price snapshots go over gRPC-Web unary calls to the API's anonymous read
// endpoints. GrpcChannel can't parse the https+http service-discovery scheme (that
// resolution lives in the HttpClient handler pipeline), so resolve the api endpoint from
// the configuration the AppHost injects via WithReference. GrpcWebMode.GrpcWeb works over
// HTTP/1.1 everywhere, which matters in the integration-test topology where the API
// listens on a plaintext endpoint (no h2c alongside HTTP/1.1).
#pragma warning disable S1075 // Standalone-run fallback to the api's launch-profile address.
var catalogAddress = new Uri(
    builder.Configuration["services:api:https:0"]
    ?? builder.Configuration["services:api:http:0"]
    ?? "https://localhost:7100");
#pragma warning restore S1075
// HTTP/1.1 exactly: gRPC-Web defaults to HTTP/2, and Kestrel can't speak h2c alongside
// HTTP/1.1 on a plaintext endpoint.
builder.Services
    .AddGrpcClient<ComponentService.ComponentServiceClient>(options => options.Address = catalogAddress)
    .ConfigureChannel(channel =>
    {
      channel.HttpVersion = System.Net.HttpVersion.Version11;
      channel.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new GrpcWebHandler(GrpcWebMode.GrpcWeb, new SocketsHttpHandler()));
builder.Services
    .AddGrpcClient<BikeBuildService.BikeBuildServiceClient>(options => options.Address = catalogAddress)
    .ConfigureChannel(channel =>
    {
      channel.HttpVersion = System.Net.HttpVersion.Version11;
      channel.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new GrpcWebHandler(GrpcWebMode.GrpcWeb, new SocketsHttpHandler()));
builder.Services.AddScoped<CatalogPricingService>();

// JWT bearer auth guards the back-office orders query; the guest-checkout operations stay
// anonymous. Config-gated so standalone runs (EF design time, schema export) need no Auth0
// settings - without them the protected field simply can't be executed.
var auth0Authority = builder.Configuration["Auth0:Authority"];
if (auth0Authority is not null)
{
  builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
      .AddJwtBearer(options =>
      {
        options.Authority = auth0Authority;
        options.Audience = builder.Configuration["Auth0:Audience"];
        // False only in the integration-test environment, where the stub OIDC issuer is plain http.
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth0:RequireHttpsMetadata", true);
        options.TokenValidationParameters.NameClaimType = "sub";
      });
}
builder.Services.AddAuthorization();

// The signed-in web app queries this GraphQL endpoint straight from the browser.
var webAppOrigins = builder.Configuration.GetSection("WebAppOrigins").Get<string[]>()
    ?? ["https://localhost:7200", "http://localhost:7201"];
builder.Services.AddCors(options => options.AddPolicy("BlazorWasmClient", policy =>
    policy.WithOrigins(webAppOrigins).AllowAnyMethod().AllowAnyHeader()));

// Query/Mutation are static classes extending empty root types - the shape HotChocolate
// expects for static resolver methods.
builder.AddGraphQL()
    .AddAuthorization()
    .AddQueryType()
    .AddMutationType()
    .AddTypeExtension(typeof(Query))
    .AddTypeExtension(typeof(Mutation))
    .RegisterDbContextFactory<OrdersDbContext>();

var app = builder.Build();

// Local dev/test only: apply EF migrations at startup, same convention as BikeBuilder.API
// (production would run migrations as a deploy step). Skipped when no connection string is
// injected (standalone runs for schema export or EF design-time work).
if (app.Environment.IsDevelopment() && ordersConnectionString is not null)
{
  await using var db = await app.Services.GetRequiredService<IDbContextFactory<OrdersDbContext>>()
      .CreateDbContextAsync();
  await db.Database.MigrateAsync();
}

app.UseCors("BlazorWasmClient");
if (auth0Authority is not null)
{
  app.UseAuthentication();
  app.UseAuthorization();
}

// The GraphQL endpoint itself stays anonymous (guest checkout); the Nitro IDE is dev-only.
app.MapGraphQL()
    .WithOptions(options => options.Tool.Enable = app.Environment.IsDevelopment())
    .RequireCors("BlazorWasmClient");

// Stays anonymous - the AppHost uses it as the health probe.
app.MapGet("/", () => "BikeBuilder.API.Orders — GraphQL endpoint at /graphql.");
app.MapDefaultEndpoints();

await app.RunAsync();
