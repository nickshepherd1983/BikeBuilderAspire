using BikeBuilder.API.Protos;
using BikeBuilder.Web.Public.Components;
using BikeBuilder.Web.Public.GraphQL;
using BikeBuilder.Web.Public.Services;
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
);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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
// HttpVersion 1.1: GrpcWebHandler still tries HTTP/2 by default, and Kestrel can't speak
// h2c alongside HTTP/1.1 on a plaintext endpoint.
builder.Services
    .AddGrpcClient<ComponentService.ComponentServiceClient>(options => options.Address = catalogAddress)
    .ConfigurePrimaryHttpMessageHandler(() =>
        new GrpcWebHandler(GrpcWebMode.GrpcWeb, new SocketsHttpHandler()) { HttpVersion = new Version(1, 1) });
builder.Services
    .AddGrpcClient<BikeBuildService.BikeBuildServiceClient>(options => options.Address = catalogAddress)
    .ConfigurePrimaryHttpMessageHandler(() =>
        new GrpcWebHandler(GrpcWebMode.GrpcWeb, new SocketsHttpHandler()) { HttpVersion = new Version(1, 1) });
builder.Services.AddScoped<CatalogClient>();
// Plain HttpClient for the component-image proxy endpoint below.
builder.Services.AddHttpClient("catalog-images", client => client.BaseAddress = catalogAddress);

// StrawberryShake-generated orders client, defined by the operation documents in the
// GraphQL folder. Also served by IHttpClientFactory, so the same service discovery applies.
builder.Services.AddOrdersClient()
    .ConfigureHttpClient(client => client.BaseAddress = ordersGraphQLAddress);
builder.Services.AddScoped<OrderState>();

// The WASM app's order-toast hub connection is cross-origin; SignalR negotiation needs
// explicit origins + credentials. WebAppOrigins is injected by the AppHost.
var webAppOrigins = builder.Configuration.GetSection("WebAppOrigins").Get<string[]>()
    ?? ["https://localhost:7200", "http://localhost:7201"];
builder.Services.AddCors(options => options.AddPolicy("WasmNotificationsClient", policy =>
    policy.WithOrigins(webAppOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/Error", createScopeForErrors: true);
  // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
  app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseCors();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

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
