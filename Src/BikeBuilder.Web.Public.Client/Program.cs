using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

// The WebAssembly half of the storefront's InteractiveAuto setup: this DI container only
// exists once a visitor's browser has the runtime cached and pages render client-side.
// Base addresses come from wwwroot/appsettings*.json - the browser can't read the Aspire
// service-discovery config the server half uses (same constraint as BikeBuilder.Web).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
// No RootComponents registrations: in a Blazor Web App the server delivers the component tree.

builder.Services.AddMudServices();

// Catalog reads go over the API's anonymous gRPC-Web endpoints, straight from the browser.
// HttpClientHandler (not SocketsHttpHandler) is the browser-capable handler, and the csproj
// disables WASM streaming responses - both per the BikeBuilder.Web precedent.
var apiBaseAddress = builder.Configuration["ApiBaseAddress"] ?? "https://localhost:7100";
builder.Services.AddScoped(_ => GrpcChannel.ForAddress(apiBaseAddress, new GrpcChannelOptions
{
  HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler())
}));
builder.Services.AddScoped(sp => new ComponentService.ComponentServiceClient(sp.GetRequiredService<GrpcChannel>()));
builder.Services.AddScoped(sp => new BikeBuildService.BikeBuildServiceClient(sp.GetRequiredService<GrpcChannel>()));
builder.Services.AddScoped<CatalogClient>();

// Guest checkout talks GraphQL directly to the Orders service (all storefront operations
// are anonymous). Same generated StrawberryShake client the server half uses - only the
// HttpClient it rides on differs.
var ordersApiBaseAddress = builder.Configuration["OrdersApiBaseAddress"] ?? "https://localhost:7400";
builder.Services.AddOrdersClient()
    .ConfigureHttpClient(client => client.BaseAddress = new Uri(new Uri(ordersApiBaseAddress), "/graphql"));

builder.Services.AddScoped<OrderState>();
builder.Services.AddScoped<INotificationsHubUrlProvider, BrowserNotificationsHubUrlProvider>();

await builder.Build().RunAsync();
