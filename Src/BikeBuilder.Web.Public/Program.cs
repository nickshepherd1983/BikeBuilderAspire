using BikeBuilder.Contracts.Grpc;
using BikeBuilder.Web.Public.Components;
using Grpc.Net.Client.Web;
using MudBlazor.Services;

// Azure SDK messaging tracing (the ServiceBusProcessor.ProcessMessage span that continues
// the API's trace into this app) is still behind this experimental switch. Must be set
// before any ServiceBusClient is constructed.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(configureTracing: tracing => tracing
    .AddSource("BikeBuilder.Web.Public")              // custom broadcast span in the listener
    .AddSource("Microsoft.AspNetCore.SignalR.Server") // client-invoked hub methods, if any appear
    // Blazor's circuit, navigation and event-handler spans: on the first-visit server circuit a
    // button click has no HTTP request of its own, so without these every outbound GraphQL or
    // gRPC call from an event handler would start a fresh trace.
    .AddSource("Microsoft.AspNetCore.Components")
    .AddSource("Microsoft.AspNetCore.Components.Server.Circuits")
);
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(
    "Microsoft.AspNetCore.Components",
    "Microsoft.AspNetCore.Components.Lifecycle",
    "Microsoft.AspNetCore.Components.Server.Circuits"));

// Add services to the container. InteractiveAuto needs both runtimes registered: the first
// visit interacts over a server circuit while the WebAssembly runtime downloads in the
// background, and later visits render client-side out of BikeBuilder.Web.Public.Client.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddMudServices();
builder.Services.AddSignalR();

// The "servicebus" connection string is injected by the AppHost (WithReference).
builder.AddAzureServiceBusClient("servicebus");
builder.Services.AddHostedService<ServiceBusListenerBackgroundService>();

// Storefront catalog: gRPC-Web unary calls to the API's anonymous read endpoints.
// GrpcChannel can't parse the https+http service-discovery scheme (that resolution lives
// in the HttpClient handler pipeline), so resolve the api endpoint from the configuration
// the AppHost injects via WithReference. The orders GraphQL client is a plain HttpClient,
// so its logical service-discovery address resolves normally. GrpcWebMode.GrpcWeb works
// over HTTP/1.1 everywhere, so no h2c is needed on the test topology's plaintext endpoint.
#pragma warning disable S1075 // Logical service-discovery name + a standalone-run fallback address.
var catalogAddress = new Uri(
    builder.Configuration["services:api:https:0"]
    ?? builder.Configuration["services:api:http:0"]
    ?? "https://localhost:7100");
var ordersGraphQLAddress = new Uri("https+http://orders/graphql");
#pragma warning restore S1075
// HTTP/1.1 exactly: gRPC-Web defaults to HTTP/2, and Kestrel can't speak h2c alongside
// HTTP/1.1 on a plaintext endpoint. The channel's ServiceConfig retries Unavailable on the
// read methods (see CatalogGrpcRetry); the factory's standard resilience handler still wraps
// each attempt with its timeouts and circuit breaker but no longer retries these POSTs itself.
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
builder.Services.AddScoped<CatalogClient>();
// Plain HttpClient for the component-image proxy endpoint below.
builder.Services.AddHttpClient("catalog-images", client => client.BaseAddress = catalogAddress);

// StrawberryShake-generated orders client, defined by the operation documents in the
// GraphQL folder. Also served by IHttpClientFactory, so the same service discovery applies.
// OrdersClientResilience swaps the shared default handler for one whose retry is limited to
// connection failures - the mutations aren't safe to replay after a timeout.
builder.Services.AddOrdersClient()
    .ConfigureHttpClient(client => client.BaseAddress = ordersGraphQLAddress, OrdersClientResilience.Configure);
builder.Services.AddScoped<OrderState>();
// The server circuit reaches browser localStorage over JS interop, same as the WASM runtime.
builder.Services.AddScoped<IOrderIdStorage, BrowserOrderIdStorage>();
builder.Services.AddScoped<IProductImageUrlProvider, RelativeProductImageUrlProvider>();
// The browser resolves the notification hub from its own origin; on the circuit the page
// runs in this process, which needs Kestrel's actual bound address instead.
builder.Services.AddScoped<INotificationsHubUrlProvider, ServerNotificationsHubUrlProvider>();

// The WASM app's order-toast hub connection is cross-origin; SignalR negotiation needs
// explicit origins + credentials. WebAppOrigins is injected by the AppHost.
var webAppOrigins = builder.Configuration.GetSection("WebAppOrigins").Get<string[]>()
    ?? ["https://localhost:7200", "http://localhost:7201"];
builder.Services.AddCors(options => options.AddPolicy("WasmNotificationsClient", policy =>
    policy.WithOrigins(webAppOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
  app.UseWebAssemblyDebugging();
}
else
{
  app.UseExceptionHandler("/Error", createScopeForErrors: true);
  // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
  app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseTraceIdResponseHeader();
app.UseHttpsRedirection();

app.UseCors();
app.UseAntiforgery();

app.MapStaticAssets();
// CatalogClient as the marker type: both assemblies share the root namespace, so Program
// and _Imports would be ambiguous here.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(CatalogClient).Assembly);

app.MapHub<NotificationHub>("/hubs/notifications").RequireCors("WasmNotificationsClient");

// Serves catalog images to the storefront same-origin; the browser never needs the API's
// address (its <img> tags can't attach headers, and cross-origin adds the localhost vs
// 127.0.0.1 Chromium flakiness the integration tests dodge on principle).
app.MapGet("/store/components/{id:int}/image", async (int id, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
{
  var http = httpClientFactory.CreateClient("catalog-images");
  var response = await http.GetAsync($"/api/components/{id}/image", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
  if (!response.IsSuccessStatusCode)
    return Results.NotFound();

  return Results.Stream(await response.Content.ReadAsStreamAsync(cancellationToken),
      response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream");
});

app.MapDefaultEndpoints();

await app.RunAsync();
