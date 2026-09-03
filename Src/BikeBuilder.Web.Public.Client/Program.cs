using BikeBuilder.Contracts.Grpc;
using BikeBuilder.Contracts.Tracing;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

// The WebAssembly half of the storefront's InteractiveAuto setup: this DI container only
// exists once a visitor's browser has the runtime cached and pages render client-side.
// Base addresses come from wwwroot/appsettings*.json - the browser can't read the Aspire
// service-discovery config the server half uses (same constraint as BikeBuilder.Web.Admin).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
// No RootComponents registrations: in a Blazor Web App the server delivers the component tree.

builder.Services.AddMudServices();

// Catalog reads go over the API's anonymous gRPC-Web endpoints, straight from the browser.
// HttpClientHandler (not SocketsHttpHandler) is the browser-capable handler, and the csproj
// disables WASM streaming responses - both per the BikeBuilder.Web.Admin precedent.
var apiBaseAddress = builder.Configuration["ApiBaseAddress"] ?? "http://localhost:7500";
// TraceContextHandler mints the traceparent each call carries, so the server-side trace starts
// with the shopper's action rather than at the gateway - see BikeBuilder.Contracts.Tracing.
builder.Services.AddScoped(_ => GrpcChannel.ForAddress(apiBaseAddress, new GrpcChannelOptions
{
  HttpHandler = new TraceContextHandler { InnerHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler()) },
  // Retries Unavailable on the catalog read methods - see CatalogGrpcRetry.
  ServiceConfig = CatalogGrpcRetry.CreateServiceConfig()
}));
builder.Services.AddScoped(sp => new ComponentService.ComponentServiceClient(sp.GetRequiredService<GrpcChannel>()));
builder.Services.AddScoped(sp => new BikeBuildService.BikeBuildServiceClient(sp.GetRequiredService<GrpcChannel>()));
builder.Services.AddScoped<CatalogClient>();

// Guest checkout talks GraphQL directly to the Orders service (all storefront operations
// are anonymous). Same generated StrawberryShake client the server half uses - only the
// HttpClient it rides on differs.
var ordersApiBaseAddress = builder.Configuration["OrdersApiBaseAddress"] ?? "http://localhost:7500/orders";
builder.Services.AddOrdersClient()
    // Trailing slash + relative "graphql": the base address carries the gateway's /orders
    // prefix, and a rooted "/graphql" would replace the whole path rather than append.
    // OrdersClientResilience: connection-failure retries + timeouts, shared with the server half.
    // The trace handler goes on first (outermost), so one trace id covers every retry.
    .ConfigureHttpClient(
        client => client.BaseAddress = new Uri(new Uri(WithTrailingSlash(ordersApiBaseAddress)), "graphql"),
        clientBuilder =>
        {
          clientBuilder.AddHttpMessageHandler(() => new TraceContextHandler());
          OrdersClientResilience.Configure(clientBuilder);
        });

builder.Services.AddScoped<OrderState>();
builder.Services.AddScoped<IOrderIdStorage, BrowserOrderIdStorage>();
builder.Services.AddScoped<IProductImageUrlProvider, RelativeProductImageUrlProvider>();
builder.Services.AddScoped<INotificationsHubUrlProvider, BrowserNotificationsHubUrlProvider>();

await builder.Build().RunAsync();

static string WithTrailingSlash(string address) => address.EndsWith('/') ? address : address + "/";
