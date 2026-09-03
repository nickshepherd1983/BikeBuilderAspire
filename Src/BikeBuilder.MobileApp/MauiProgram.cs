using BikeBuilder.Contracts.Grpc;
using BikeBuilder.Contracts.Tracing;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace BikeBuilder.MobileApp;

// The native shell around the shared storefront (BikeBuilder.Web.Public.Shared): the DI
// recipe mirrors BikeBuilder.Web.Public.Client's Program.cs, swapping the browser-bound
// pieces (localStorage, origin-relative URLs) for platform implementations.
public static class MauiProgram
{
  public static MauiApp CreateMauiApp()
  {
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiApp<App>();
    builder.Services.AddMauiBlazorWebView();
#if DEBUG
    builder.Services.AddBlazorWebViewDeveloperTools();
    builder.Logging.AddDebug();
#endif

    builder.Services.AddMudServices();

    // Catalog reads go through the gateway's anonymous gRPC-Web endpoints, same as the
    // browser does. gRPC-Web rather than native gRPC on purpose: the gateway (APIM or YARP)
    // fronts gRPC-Web over HTTP/1.1, so one wire format serves every client. HTTP/1.1
    // exactly, same as BikeBuilder.Web.Public's server half: a native handler defaults gRPC
    // to HTTP/2, and the gateway's plaintext endpoint answers that with HTTP_1_1_REQUIRED.
    // TraceContextHandler mints the traceparent each call carries - see BikeBuilder.Contracts.Tracing.
    builder.Services.AddScoped(_ => GrpcChannel.ForAddress(AppEnvironment.ApiBaseAddress, new GrpcChannelOptions
    {
      HttpHandler = new TraceContextHandler { InnerHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler()) },
      HttpVersion = System.Net.HttpVersion.Version11,
      HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
      // Retries Unavailable on the catalog read methods - see CatalogGrpcRetry. Matters more
      // on a phone than anywhere else: mobile networks drop connections routinely.
      ServiceConfig = CatalogGrpcRetry.CreateServiceConfig()
    }));
    builder.Services.AddScoped(sp => new ComponentService.ComponentServiceClient(sp.GetRequiredService<GrpcChannel>()));
    builder.Services.AddScoped(sp => new BikeBuildService.BikeBuildServiceClient(sp.GetRequiredService<GrpcChannel>()));
    builder.Services.AddScoped<CatalogClient>();

    // Guest checkout talks GraphQL to the Orders service through the gateway. Trailing
    // slash + relative "graphql": the base address carries the gateway's /orders prefix,
    // and a rooted "/graphql" would replace the whole path rather than append.
    // OrdersClientResilience: connection-failure retries + timeouts, shared with the web heads.
    // The trace handler goes on first (outermost), so one trace id covers every retry.
    builder.Services.AddOrdersClient()
        .ConfigureHttpClient(
            client => client.BaseAddress = new Uri(new Uri(WithTrailingSlash(AppEnvironment.OrdersApiBaseAddress)), "graphql"),
            clientBuilder =>
            {
              clientBuilder.AddHttpMessageHandler(() => new TraceContextHandler());
              OrdersClientResilience.Configure(clientBuilder);
            });

    builder.Services.AddScoped<OrderState>();
    builder.Services.AddScoped<IOrderIdStorage, PreferencesOrderIdStorage>();
    builder.Services.AddScoped<IProductImageUrlProvider, ApiProductImageUrlProvider>();
    builder.Services.AddScoped<INotificationsHubUrlProvider, MauiNotificationsHubUrlProvider>();

    return builder.Build();
  }

  static string WithTrailingSlash(string address) => address.EndsWith('/') ? address : address + "/";
}
