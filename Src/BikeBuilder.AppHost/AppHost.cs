using Microsoft.Extensions.Configuration;

// Test mode (the integration-test fixture passes IntegrationTest=true) swaps the real Auth0
// tenant for a stub OIDC container, pins every app to a fixed 18xxx port the Playwright
// browser can rely on, and keeps containers session-scoped instead of persistent.
var builder = DistributedApplication.CreateBuilder(args);
var isTest = builder.Configuration.GetValue("IntegrationTest", false);
var lifetime = isTest ? ContainerLifetime.Session : ContainerLifetime.Persistent;

// 127.0.0.1 rather than "localhost" in every browser-facing test address - on Windows/Docker
// Desktop the .NET HttpClient and Chromium have been observed resolving "localhost"
// differently, with only one of the two reliably connecting (see the old Testcontainers
// fixture's history). The issuer the browser uses must equal the token's iss claim, so the
// API and Ratings validate against this same URI.
const string TestWebBaseAddress = "http://127.0.0.1:18200";
const string TestOidcIssuer = "http://127.0.0.1:18400";
const string OidcAudience = "bikebuilder-api";
const string OidcClientId = "bikebuilder-web";
const string Auth0Authority = "https://dev-s5bdd188garbulmp.us.auth0.com";
const string Auth0Audience = "https://bikebuilder-api";

// --- Backing services -------------------------------------------------------------------

// 2025+ required: the Components.Information column uses the native json type.
var sql = builder.AddSqlServer("sql")
    .WithImageTag("2025-latest")
    .WithLifetime(lifetime);
if (!isTest)
  sql.WithDataVolume();
var db = sql.AddDatabase("BikeBuilderDb");
// The Orders bounded context gets its own logical database on the same server container.
var ordersDb = sql.AddDatabase("BikeBuilderOrdersDb");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite =>
    {
      azurite.WithLifetime(lifetime);
      if (!isTest)
        azurite.WithDataVolume();
    });
var componentImages = storage.AddBlobContainer("component-images");

var serviceBus = builder.AddAzureServiceBus("servicebus")
    .RunAsEmulator(emulator => emulator.WithLifetime(lifetime));
serviceBus.AddServiceBusQueue(BikeBuilder.Contracts.Messaging.ServiceBusQueueNames.Notifications);

var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsEmulator(emulator =>
    {
      emulator.WithLifetime(lifetime);
      if (!isTest)
        emulator.WithDataVolume();
    });
// The container resource gets its own name because the Functions app already claims the
// resource name "ratings" - the actual Cosmos container keeps that name via the third arg.
var ratingsContainer = cosmos.AddCosmosDatabase("bikebuilder").AddContainer("ratings-container", "/bikeBuildId", "ratings");

// --- Test-only stub OIDC issuer (stands in for Auth0) -----------------------------------

IResourceBuilder<ContainerResource>? oidc = null;
if (isTest)
{
  // 0.8.6 (Duende IdentityServer 6.3 on .NET 6) predates the image's .NET 8 rebase, so the
  // container listens on port 80; its quickstart login form uses "Input.Username"/
  // "Input.Password" - see NavigationHelper's login handling in the test project.
  oidc = builder.AddContainer("oidc-mock", "ghcr.io/soluto/oidc-server-mock", "0.8.6")
      .WithHttpEndpoint(port: 18400, targetPort: 80)
      .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
      // CookieSameSiteMode=Lax: the default of SameSite=None requires Secure, and Chromium
      // silently drops such cookies over plain http - the login POST would succeed but the
      // browser would return to /connect/authorize with no session, looping forever.
      .WithEnvironment("SERVER_OPTIONS_INLINE",
          $$$"""{"IssuerUri":"{{{TestOidcIssuer}}}","AccessTokenJwtType":"JWT","Authentication":{"CookieSameSiteMode":"Lax"}}""")
      .WithEnvironment("API_SCOPES_INLINE",
          $$"""[{"Name":"{{OidcAudience}}"}]""")
      // UserClaims: puts the user's name claim into access tokens for this API, the way a
      // real Auth0 tenant would via an Action - the ratings service reads it for userName.
      .WithEnvironment("API_RESOURCES_INLINE",
          $$"""[{"Name":"{{OidcAudience}}","Scopes":["{{OidcAudience}}"],"UserClaims":["name"]}]""")
      .WithEnvironment("CLIENTS_CONFIGURATION_INLINE",
          $$"""
          [{
            "ClientId": "{{OidcClientId}}",
            "AllowedGrantTypes": ["authorization_code"],
            "RequirePkce": true,
            "RequireClientSecret": false,
            "RedirectUris": ["{{TestWebBaseAddress}}/authentication/login-callback"],
            "PostLogoutRedirectUris": ["{{TestWebBaseAddress}}/authentication/logout-callback"],
            "AllowedCorsOrigins": ["{{TestWebBaseAddress}}"],
            "AllowedScopes": ["openid", "profile", "{{OidcAudience}}"],
            "AccessTokenType": "Jwt",
            "AllowAccessTokensViaBrowser": true
          }]
          """)
      .WithEnvironment("USERS_CONFIGURATION_INLINE",
          """
          [{
            "SubjectId": "test-user",
            "Username": "testuser",
            "Password": "password",
            "Claims": [{"Type": "name", "Value": "Test User", "ValueType": "string"}]
          }]
          """)
      .WithHttpHealthCheck("/.well-known/openid-configuration");
}

// --- Apps -------------------------------------------------------------------------------

// Dev keeps each app's launch profile (fixed ports 7100/7200/7300 + http siblings) so the
// WASM app's wwwroot/appsettings.Development.json base addresses stay valid - the browser
// can't read Aspire-injected environment variables at runtime.
var web = builder.AddProject<Projects.BikeBuilder_Web>("web",
    options => options.ExcludeLaunchProfile = isTest);
if (isTest)
{
  // Port 18200 doubles as the environment signal: the WASM app's index.html starts Blazor
  // with environment "IntegrationTest" when served from this origin, which makes it load
  // wwwroot/appsettings.IntegrationTest.json (18xxx addresses + stub OIDC). The dev server
  // can't forward a hosting environment to the browser in .NET 10.
  web.WithHttpEndpoint(port: 18200);
}
web.WithHttpHealthCheck("/");

var api = builder.AddProject<Projects.BikeBuilder_API>("api",
        options => options.ExcludeLaunchProfile = isTest)
    .WithReference(db).WaitFor(db)
    .WithReference(componentImages).WaitFor(componentImages)
    .WithReference(serviceBus).WaitFor(serviceBus);
WithWebAppOrigins(api);
if (isTest)
{
  api.WithHttpEndpoint(port: 18100)
      // ExcludeLaunchProfile drops the profile's ASPNETCORE_ENVIRONMENT=Development, and
      // Production-from-build-output breaks things a dev-run app relies on (e.g. startup
      // migrations). Tests should run the apps the way F5 does.
      .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
      .WithEnvironment("Auth0__Authority", TestOidcIssuer)
      .WithEnvironment("Auth0__Audience", OidcAudience)
      .WithEnvironment("Auth0__RequireHttpsMetadata", "false")
      .WaitFor(oidc!);
}
// After endpoint setup - a health check needs the endpoint to exist. "/" is the anonymous
// info endpoint; the real /health endpoint is Development-only, and this path doubles as
// the trace filter's excluded probe.
api.WithHttpHealthCheck("/");

// Orders: GraphQL storefront backend. References the api for catalog price snapshots at
// add-to-order time.
var orders = builder.AddProject<Projects.BikeBuilder_API_Orders>("orders",
        options => options.ExcludeLaunchProfile = isTest)
    .WithReference(ordersDb).WaitFor(ordersDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithReference(api).WaitFor(api);
if (isTest)
{
  orders.WithHttpEndpoint(port: 18600)
      .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
}
orders.WithHttpHealthCheck("/");

var ratings = builder.AddAzureFunctionsProject<Projects.BikeBuilder_API_Ratings>("ratings")
    .WithHostStorage(storage)
    .WithReference(cosmos).WaitFor(ratingsContainer)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithEnvironment("Auth0__Authority", isTest ? TestOidcIssuer : Auth0Authority)
    .WithEnvironment("Auth0__Audience", isTest ? OidcAudience : Auth0Audience)
    // The anonymous warmup endpoint returns 200 for any id, so a passing probe means the
    // Functions host + worker are up and Cosmos is reachable. (The host's own "/" homepage
    // responds 200 long before the worker is ready - don't probe that.)
    .WithHttpHealthCheck("/api/bikebuilds/warmup/ratings");
WithWebAppOrigins(ratings);
if (isTest)
{
  ratings.WithEndpoint("http", endpoint => endpoint.Port = 18500)
      .WithEnvironment("Auth0__RequireHttpsMetadata", "false")
      .WaitFor(oidc!);
}
else
{
  // Keeps wwwroot/appsettings.Development.json's RatingsApiBaseAddress (localhost:7071,
  // the func-start default this app has always used) valid.
  ratings.WithEndpoint("http", endpoint => endpoint.Port = 7071);
}

var webPublic = builder.AddProject<Projects.BikeBuilder_Web_Public>("web-public",
        options => options.ExcludeLaunchProfile = isTest)
    .WithReference(serviceBus).WaitFor(serviceBus)
    // Storefront: catalog browsing + image proxy via the api, orders via GraphQL.
    .WithReference(api).WaitFor(api)
    .WithReference(orders).WaitFor(orders);
// The WASM app connects to this app's SignalR hub for order toasts - CORS needs its origins.
WithWebAppOrigins(webPublic);
if (isTest)
{
  webPublic.WithHttpEndpoint(port: 18300)
      // Without this the app runs as Production from build (not published) output, where
      // Static Web Assets are disabled - every framework script 500s and the Blazor
      // circuit never starts.
      .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
}
webPublic.WithHttpHealthCheck("/");

// On-demand from the dashboard (or `aspire run`): seeds 1000+ components, 100 builds, and
// ratings. Explicit start because seeding is a deliberate act, not part of app startup.
builder.AddProject<Projects.BikeBuilder_DataSeeder>("dataseeder")
    .WithReference(db).WaitFor(db)
    .WithReference(cosmos).WaitFor(ratingsContainer)
    .WithExplicitStart();

await builder.Build().RunAsync();

// CORS origins must match what the browser sends byte-for-byte: the pinned test address in
// test mode, the web app's own (launch-profile-pinned) endpoints in dev.
void WithWebAppOrigins<T>(IResourceBuilder<T> resource) where T : IResourceWithEnvironment
{
  resource.WithEnvironment(context =>
  {
    if (isTest)
    {
      context.EnvironmentVariables["WebAppOrigins__0"] = TestWebBaseAddress;
      return;
    }

    context.EnvironmentVariables["WebAppOrigins__0"] = web.GetEndpoint("https");
    context.EnvironmentVariables["WebAppOrigins__1"] = web.GetEndpoint("http");
  });
}
