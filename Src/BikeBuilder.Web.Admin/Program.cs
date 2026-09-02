using BikeBuilder.Contracts.Grpc;
using BikeBuilder.Web.Admin;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Http.Resilience;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMudServices();

builder.Services.AddOidcAuthentication(options =>
{
  options.ProviderOptions.Authority = builder.Configuration["Auth0:Authority"];
  options.ProviderOptions.ClientId = builder.Configuration["Auth0:ClientId"];
  // Auth0 SPAs only support authorization code + PKCE; the library defaults to implicit flow.
  options.ProviderOptions.ResponseType = "code";
  // The audience parameter is what makes Auth0 issue a JWT access token instead of an opaque
  // one; other issuers (like the integration tests' stub) simply ignore it.
  options.ProviderOptions.AdditionalProviderParameters.Add("audience", builder.Configuration["Auth0:Audience"]!);
  // Empty against real Auth0; the tests' IdentityServer-based stub mints the aud claim from
  // API scopes rather than the audience parameter, so it needs the API scope requested here.
  foreach (var scope in (builder.Configuration["Auth0:ExtraScopes"] ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
  {
    options.ProviderOptions.DefaultScopes.Add(scope);
  }
  // The ID-token claim the principal factory turns into role claims - Auth0's namespaced
  // claim (minted by the post-login Action) or the stub issuer's plain "role" in tests.
  options.UserOptions.RoleClaim = RoleClaim.Resolve(builder.Configuration[RoleClaim.ConfigKey]);
})
// Flattens Auth0's JSON-array roles claim into one claim per role - see the factory.
.AddAccountClaimsPrincipalFactory<RolesClaimsPrincipalFactory>();

// The same policy set the services enforce, so AuthorizeRouteView/AuthorizeView gate pages,
// nav links, and home cards consistently with the backend.
builder.Services.AddAuthorizationCore(options =>
{
  foreach (var (name, allowedRoles) in Policies.All)
    options.AddPolicy(name, policy => policy.RequireRole(allowedRoles));
});

var apiBaseAddress = builder.Configuration["ApiBaseAddress"] ?? "http://localhost:7500";

// Scoped rather than singleton so the factory can resolve the scoped IAccessTokenProvider;
// in WASM there is only the root scope, so this is still effectively one channel.
builder.Services.AddScoped(sp =>
{
  var tokenProvider = sp.GetRequiredService<IAccessTokenProvider>();
  var callCredentials = CallCredentials.FromInterceptor(async (context, metadata) =>
  {
    var result = await tokenProvider.RequestAccessToken();
    if (result.TryGetToken(out var token))
    {
      metadata.Add("Authorization", $"Bearer {token.Value}");
    }
    // No token: the call goes out unauthenticated and the API answers with
    // RpcException(Unauthenticated). Protected pages sit behind AuthorizeRouteView,
    // so this only happens in edge races around login/logout.
  });

  var apiUri = new Uri(apiBaseAddress);
  var isHttps = apiUri.Scheme == Uri.UriSchemeHttps;
  return GrpcChannel.ForAddress(apiUri, new GrpcChannelOptions
  {
    HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler()),
    Credentials = isHttps
        ? ChannelCredentials.Create(new SslCredentials(), callCredentials)
        : ChannelCredentials.Create(ChannelCredentials.Insecure, callCredentials),
    // Required to send bearer tokens over the http:// address the Testcontainers
    // environment uses; the https dev/prod path never sets it.
    UnsafeUseInsecureChannelCallCredentials = !isHttps,
    // Retries Unavailable on the catalog read methods only - see CatalogGrpcRetry.
    ServiceConfig = CatalogGrpcRetry.CreateServiceConfig()
  });
});

builder.Services.AddScoped(sp => new ComponentService.ComponentServiceClient(sp.GetRequiredService<GrpcChannel>()));
builder.Services.AddScoped(sp => new BikeBuildService.BikeBuildServiceClient(sp.GetRequiredService<GrpcChannel>()));

// The REST/GraphQL clients come from IHttpClientFactory so they can carry the standard
// resilience handler (retries with backoff, circuit breaker, per-attempt and total timeouts).
// Retries are limited to safe methods: the image upload posts a browser stream that can't be
// replayed, and a repeated POST would create a second rating or user. What this buys is the
// GETs - list pages, the polled in-process orders, and the intermittent "Failed to fetch" the
// csproj comment describes - recovering on their own.
AddAuthorizedClient<ComponentImageClient>(apiBaseAddress, apiBaseAddress);

// Admin-only user administration; same origin and token handling as the image client.
AddAuthorizedClient<AdminClient>(apiBaseAddress, apiBaseAddress);

// These two base addresses carry the gateway's /ratings and /orders path prefixes, so the
// clients use relative request paths against them. A trailing slash is required for that:
// without it, Uri composition drops the last path segment (the prefix). authorizedUrls
// prefix-matches, so the un-slashed configured value still covers every sub-path.
var ratingsApiBaseAddress = builder.Configuration["RatingsApiBaseAddress"] ?? "http://localhost:7500/ratings";
AddAuthorizedClient<RatingsClient>(WithTrailingSlash(ratingsApiBaseAddress), ratingsApiBaseAddress);

var ordersApiBaseAddress = builder.Configuration["OrdersApiBaseAddress"] ?? "http://localhost:7500/orders";
AddAuthorizedClient<OrdersClient>(WithTrailingSlash(ordersApiBaseAddress), ordersApiBaseAddress);

await builder.Build().RunAsync();

static string WithTrailingSlash(string address) => address.EndsWith('/') ? address : address + "/";

// A typed client whose requests carry the user's access token (the documented Blazor WASM
// pattern: AuthorizationMessageHandler resolved from the handler's own scope) plus the
// standard resilience handler with unsafe-method retries disabled.
void AddAuthorizedClient<TClient>(string baseAddress, string authorizedUrl) where TClient : class =>
    builder.Services.AddHttpClient<TClient>(client => client.BaseAddress = new Uri(baseAddress))
        .AddHttpMessageHandler(sp => sp.GetRequiredService<AuthorizationMessageHandler>()
            .ConfigureHandler(authorizedUrls: [authorizedUrl]))
        .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
