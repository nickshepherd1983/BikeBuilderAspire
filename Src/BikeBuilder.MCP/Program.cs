using BikeBuilder.Contracts.Grpc;
using BikeBuilder.MCP.Tools;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ModelContextProtocol.AspNetCore;

// A Model Context Protocol server exposing read-only tools over the catalog (components, bike
// builds), orders and ratings. It owns no data: every tool goes through the same service
// surfaces the web apps use, so the bounded contexts stay behind their APIs and the services'
// own authorization applies (the caller's bearer token is forwarded to the orders service).
var builder = WebApplication.CreateBuilder(args);

// "/" is the AppHost health probe - not worth a trace per poll.
builder.AddServiceDefaults(aspNetCoreTraceFilter: context => context.Request.Path != "/");

// Catalog reads go over gRPC-Web unary calls to the API's anonymous read endpoints - the same
// registration BikeBuilder.API.Orders uses for its price snapshots (see the comments there for
// why the address is resolved by hand and why HTTP/1.1 is pinned).
#pragma warning disable S1075 // Standalone-run fallback to the api's launch-profile address.
var catalogAddress = new Uri(
    builder.Configuration["services:api:https:0"]
    ?? builder.Configuration["services:api:http:0"]
    ?? "https://localhost:7100");
#pragma warning restore S1075
builder.Services
    .AddGrpcClient<ComponentService.ComponentServiceClient>(options => options.Address = catalogAddress)
    .ConfigureChannel(channel =>
    {
      channel.HttpVersion = System.Net.HttpVersion.Version11;
      channel.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
      channel.ServiceConfig = CatalogGrpcRetry.CreateServiceConfig();
    })
    .ConfigurePrimaryHttpMessageHandler(() => new GrpcWebHandler(GrpcWebMode.GrpcWeb, new SocketsHttpHandler()));
builder.Services
    .AddGrpcClient<BikeBuildService.BikeBuildServiceClient>(options => options.Address = catalogAddress)
    .ConfigureChannel(channel =>
    {
      channel.HttpVersion = System.Net.HttpVersion.Version11;
      channel.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
      channel.ServiceConfig = CatalogGrpcRetry.CreateServiceConfig();
    })
    .ConfigurePrimaryHttpMessageHandler(() => new GrpcWebHandler(GrpcWebMode.GrpcWeb, new SocketsHttpHandler()));

// Orders and ratings are plain HTTP behind Aspire service discovery (the https+http scheme
// prefers the https endpoint and falls back to http, which is what the Functions host offers).
// The orders client carries the caller's bearer token forward: the back-office order queries
// are role-gated there, and this server holds no credentials of its own.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<BearerForwardingHandler>();
#pragma warning disable S1075 // Service-discovery names, not real hosts - resolved by the handler pipeline.
builder.Services.AddHttpClient<OrdersGraphQLClient>(client => client.BaseAddress = new Uri("https+http://orders/"))
    .AddHttpMessageHandler<BearerForwardingHandler>();
builder.Services.AddHttpClient<RatingsHttpClient>(client => client.BaseAddress = new Uri("https+http://ratings/"));
#pragma warning restore S1075

// JWT bearer validation of the same Auth0 (or test-stub) tokens the other services accept.
// Config-gated like the orders service so the server still starts standalone with no issuer.
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
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "sub";
        options.TokenValidationParameters.RoleClaimType = RoleClaim.Resolve(builder.Configuration[RoleClaim.ConfigKey]);
      });
}
builder.Services.AddAuthorization(options =>
{
  foreach (var (name, allowedRoles) in Policies.All)
    options.AddPolicy(name, policy => policy.RequireRole(allowedRoles));
});

// Mcp:AllowAnonymous (true in appsettings.Development.json) lets local IDE clients - Claude
// Code, VS Code - connect without a token. The chat host always sends the signed-in user's
// token regardless, so the orders tools keep working for it either way; an anonymous caller
// simply gets a "sign in required" answer from those tools.
var allowAnonymous = auth0Authority is null || builder.Configuration.GetValue("Mcp:AllowAnonymous", false);

// Stateless: no server-to-client requests (sampling, elicitation) are used, so every call is a
// self-contained POST - no session affinity, and clients that never send Mcp-Session-Id work.
builder.Services.AddMcpServer(options => options.ServerInfo = new() { Name = "bikebuilder", Version = "1.0.0" })
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools<DataGuideTools>()
    .WithTools<CatalogTools>()
    .WithTools<OrdersTools>()
    .WithTools<RatingsTools>();

var app = builder.Build();

// Unhandled exceptions become ProblemDetails with a traceId; every response gets X-Trace-Id.
app.UseExceptionHandler();
app.UseTraceIdResponseHeader();

if (auth0Authority is not null)
{
  app.UseAuthentication();
  app.UseAuthorization();
}

var mcp = app.MapMcp("/mcp");
if (!allowAnonymous)
  mcp.RequireAuthorization();

// Stays anonymous - the AppHost uses it as the health probe.
app.MapGet("/", () => "BikeBuilder.MCP — Model Context Protocol endpoint at /mcp (Streamable HTTP).");
app.MapDefaultEndpoints();

await app.RunAsync();
