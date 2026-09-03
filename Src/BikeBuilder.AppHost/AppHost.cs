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
const string TestWebPublicBaseAddress = "http://127.0.0.1:18300";
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

// Unsubmitted guest carts. Everything in here is disposable and expires within the hour, so
// unlike sql/cosmos it gets no data volume even outside tests - a restart losing in-flight
// carts is the same outcome as the TTL firing.
var cache = builder.AddRedis("cache").WithLifetime(lifetime);

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
// The emulator reads its queue list once, at container start. Outside tests the container is
// persistent, so after adding a queue here the existing servicebus emulator (and its SQL Edge
// companion) must be removed once - `docker rm -f` or the dashboard - or the queue won't exist.
serviceBus.AddServiceBusQueue(BikeBuilder.Contracts.Messaging.ServiceBusQueueNames.OrderEmails);

// Catches every email the notifications Function sends locally; nothing leaves the machine.
// UI + REST API on the http endpoint (the order smoke test reads the inbox through it), SMTP
// on the tcp one. IMAP/POP3 off (unused listeners); no volume - a fresh inbox per container.
var smtp4dev = builder.AddContainer("smtp4dev", "rnwood/smtp4dev", "v3")
    .WithHttpEndpoint(port: isTest ? 18000 : 7800, targetPort: 80, name: "http")
    .WithEndpoint(port: isTest ? 18025 : 7825, targetPort: 25, name: "smtp", scheme: "tcp")
    .WithEnvironment("ServerOptions__HostName", "smtp4dev.local")
    .WithEnvironment("ServerOptions__ImapPort", "0")
    .WithEnvironment("ServerOptions__Pop3Port", "0")
    // The server-status endpoint: anonymous by default, and it only answers once the SMTP
    // listener the Function needs is up.
    .WithHttpHealthCheck("/api/Server")
    .WithLifetime(lifetime);

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
      //
      // EnableCheckSessionEndpoint=false: with check_session_iframe advertised, the WASM
      // app's oidc-client starts its OP session monitor, which against this stub degenerates
      // into an endless silent-reauth loop (authorize?prompt=none -> login-callback booting
      // the whole app in a hidden iframe, 2-3 times per second) that burns CPU and wedges
      // real token requests - observed as rating submissions hanging forever. Dropping the
      // endpoint from discovery disables the monitor; real Auth0 doesn't advertise one either.
      .WithEnvironment("SERVER_OPTIONS_INLINE",
          $$$"""{"IssuerUri":"{{{TestOidcIssuer}}}","AccessTokenJwtType":"JWT","Authentication":{"CookieSameSiteMode":"Lax"},"Endpoints":{"EnableCheckSessionEndpoint":false}}""")
      .WithEnvironment("API_SCOPES_INLINE",
          $$"""[{"Name":"{{OidcAudience}}"}]""")
      // UserClaims: puts the user's name and role claims into access tokens for this API,
      // the way a real Auth0 tenant would via an Action - the ratings service reads name
      // for userName, and every service reads role for authorization.
      .WithEnvironment("API_RESOURCES_INLINE",
          $$"""[{"Name":"{{OidcAudience}}","Scopes":["{{OidcAudience}}"],"UserClaims":["name","role"]}]""")
      // The WASM app builds its principal from the ID token, so role has to be an identity
      // claim too (paired with the client's AlwaysIncludeUserClaimsInIdToken below). NOTE:
      // identity resources use "ClaimTypes" in this image's config model, unlike the API
      // resources' "UserClaims" - an unknown property crashes the container at startup.
      .WithEnvironment("IDENTITY_RESOURCES_INLINE",
          """[{"Name":"roles","ClaimTypes":["role"]}]""")
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
            "AllowedScopes": ["openid", "profile", "roles", "{{OidcAudience}}"],
            "AccessTokenType": "Jwt",
            "AllowAccessTokensViaBrowser": true,
            "AlwaysIncludeUserClaimsInIdToken": true
          }]
          """)
      // Admin so the existing smoke tests can keep exercising every surface; further users
      // with narrower roles are created at runtime through the Admin section (the mock's
      // POST /api/v1/user), which is itself covered by AdminSmokeTests.
      .WithEnvironment("USERS_CONFIGURATION_INLINE",
          """
          [{
            "SubjectId": "test-user",
            "Username": "testuser",
            "Password": "password",
            "Claims": [
              {"Type": "name", "Value": "Test User", "ValueType": "string"},
              {"Type": "role", "Value": "Admin", "ValueType": "string"}
            ]
          }]
          """)
      .WithHttpHealthCheck("/.well-known/openid-configuration");
}

// --- Apps -------------------------------------------------------------------------------

// Dev keeps each app's launch profile (fixed ports 7100/7200/7300 + http siblings) so the
// WASM app's wwwroot/appsettings.Development.json base addresses stay valid - the browser
// can't read Aspire-injected environment variables at runtime.
var webAdmin = builder.AddProject<Projects.BikeBuilder_Web_Admin>("web-admin",
    options => options.ExcludeLaunchProfile = isTest);
if (isTest)
{
  // Port 18200 doubles as the environment signal: the WASM app's index.html starts Blazor
  // with environment "IntegrationTest" when served from this origin, which makes it load
  // wwwroot/appsettings.IntegrationTest.json (18xxx addresses + stub OIDC). The dev server
  // can't forward a hosting environment to the browser in .NET 10.
  webAdmin.WithHttpEndpoint(port: 18200);
}
webAdmin.WithHttpHealthCheck("/");

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
      // The stub issuer mints plain "role" claims; Auth0 uses the namespaced default.
      .WithEnvironment("Auth0__RoleClaim", "role")
      // The Admin section's user store: the stub's runtime user API on the same origin.
      .WithEnvironment("UserAdmin__MockUrl", TestOidcIssuer)
      .WaitFor(oidc!);
}
// After endpoint setup - a health check needs the endpoint to exist. "/" is the anonymous
// info endpoint; the real /health endpoint is Development-only, and this path doubles as
// the trace filter's excluded probe.
api.WithHttpHealthCheck("/");

// Orders: GraphQL storefront backend. References the api for catalog price snapshots at
// add-to-order time. Auth0 guards the back-office orders query; guest checkout is anonymous.
// Draft carts live in the cache (with a TTL); only placed orders reach ordersDb.
var orders = builder.AddProject<Projects.BikeBuilder_API_Orders>("orders",
        options => options.ExcludeLaunchProfile = isTest)
    .WithReference(ordersDb).WaitFor(ordersDb)
    .WithReference(cache).WaitFor(cache)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithReference(api).WaitFor(api)
    .WithEnvironment("Auth0__Authority", isTest ? TestOidcIssuer : Auth0Authority)
    .WithEnvironment("Auth0__Audience", isTest ? OidcAudience : Auth0Audience);
WithWebAppOrigins(orders);
if (isTest)
{
  orders.WithHttpEndpoint(port: 18600)
      .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
      .WithEnvironment("Auth0__RequireHttpsMetadata", "false")
      .WithEnvironment("Auth0__RoleClaim", "role")
      .WaitFor(oidc!);
}
orders.WithHttpHealthCheck("/");

var ratings = builder.AddAzureFunctionsProject<Projects.BikeBuilder_API_Ratings>("ratings")
    .WithHostStorage(storage)
    // WithHostStorage wires the connection but doesn't gate startup: without this wait the
    // Functions host can race Azurite on a slow machine and die on an unreachable
    // AzureWebJobsStorage.
    .WaitFor(storage)
    .WithReference(cosmos).WaitFor(ratingsContainer)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithEnvironment("Auth0__Authority", isTest ? TestOidcIssuer : Auth0Authority)
    .WithEnvironment("Auth0__Audience", isTest ? OidcAudience : Auth0Audience)
    // The anonymous warmup endpoint returns 200 for any id, so a passing probe means the
    // Functions host + worker are up and Cosmos is reachable. (The host's own "/" homepage
    // responds 200 long before the worker is ready - don't probe that.)
    .WithHttpHealthCheck("/api/bikebuilds/warmup/ratings");
WithWebAppOrigins(ratings);
WithInstalledCoreTools(ratings);
if (isTest)
{
  ratings.WithEndpoint("http", endpoint => endpoint.Port = 18500)
      .WithEnvironment("Auth0__RequireHttpsMetadata", "false")
      .WithEnvironment("Auth0__RoleClaim", "role")
      .WaitFor(oidc!);
}
else
{
  // Keeps wwwroot/appsettings.Development.json's RatingsApiBaseAddress (localhost:7071,
  // the func-start default this app has always used) valid.
  ratings.WithEndpoint("http", endpoint => endpoint.Port = 7071);
}

// Order receipts. Deployed, this same Functions project also runs the Service Bus -> SignalR
// fan-out; locally that job belongs to Web.Public's own listener on the notifications queue,
// so the two SignalR functions are disabled here - a second receiver would steal the toasts,
// and there is no Azure SignalR to push to anyway.
var notifications = builder.AddAzureFunctionsProject<Projects.BikeBuilder_API_Notifications>("notifications")
    .WithHostStorage(storage)
    .WaitFor(storage)
    // Aspire's Functions integration writes the emulator connection string under the plain
    // "servicebus" key, which is exactly what [ServiceBusTrigger(Connection = "servicebus")] reads.
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WaitFor(smtp4dev)
    .WithEnvironment(context =>
    {
      var smtp = smtp4dev.GetEndpoint("smtp");
      context.EnvironmentVariables["Email__Smtp__Host"] = smtp.Property(EndpointProperty.Host);
      context.EnvironmentVariables["Email__Smtp__Port"] = smtp.Property(EndpointProperty.Port);
    })
    .WithEnvironment("Email__From__Address", "orders@bikebuilder.local")
    .WithEnvironment("Email__From__Name", "BikeBuilder")
    .WithEnvironment("AzureWebJobs.BroadcastNotification.Disabled", "true")
    .WithEnvironment("AzureWebJobs.negotiate.Disabled", "true")
    // Two Functions hosts share the one Azurite account: pin this one's host id so its lock
    // and lease blobs can't collide with the ratings host's.
    .WithEnvironment("AzureFunctionsWebHost__hostid", "bikebuilder-notifications")
    // An anonymous worker function, for the same reason ratings probes its warmup endpoint:
    // the host's "/" is up long before the worker (and its Service Bus trigger) is.
    .WithHttpHealthCheck("/api/health");
notifications.WithEndpoint("http", endpoint => endpoint.Port = isTest ? 18950 : 7072);
WithInstalledCoreTools(notifications);

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

// Under InteractiveAuto the storefront's WebAssembly half calls these two services straight
// from the browser (catalog gRPC-Web, orders GraphQL), so their CORS has to allow the
// storefront's own origins on top of the signed-in web app's.
WithStorefrontOrigins(api);
WithStorefrontOrigins(orders);

// --- AI assistant: MCP server + chat host --------------------------------------------------

// The model runs in the Ollama installed on the developer machine (GPU-accelerated there),
// not in a container, so it is modelled as a connection string - Endpoint and Model, from
// this project's appsettings.json or user secrets - rather than a hosted resource.
var ollama = builder.AddConnectionString("ollama");

// Read-only MCP tools over the catalog, orders and ratings. Owns no data: it calls the same
// three services the web apps do (api over gRPC-Web, orders GraphQL, ratings HTTP), and
// validates the same tokens so it can forward the caller's to the role-gated order queries.
var mcp = builder.AddProject<Projects.BikeBuilder_MCP>("mcp",
        options => options.ExcludeLaunchProfile = isTest)
    .WithReference(api).WaitFor(api)
    .WithReference(orders).WaitFor(orders)
    .WithReference(ratings).WaitFor(ratings)
    .WithEnvironment("Auth0__Authority", isTest ? TestOidcIssuer : Auth0Authority)
    .WithEnvironment("Auth0__Audience", isTest ? OidcAudience : Auth0Audience);
if (isTest)
{
  mcp.WithHttpEndpoint(port: 18800)
      .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
      .WithEnvironment("Auth0__RequireHttpsMetadata", "false")
      .WithEnvironment("Auth0__RoleClaim", "role")
      // Development turns anonymous access on for local IDE clients; tests exercise the
      // authenticated path the chat host uses.
      .WithEnvironment("Mcp__AllowAnonymous", "false")
      .WaitFor(oidc!);
}
mcp.WithHttpHealthCheck("/");

// The admin app's assistant backend: Ollama + the MCP tools behind an Admin-only endpoint the
// browser reaches through the gateway's /chat prefix. Its probe never touches Ollama, so the
// topology (and CI) is healthy with no model installed - the page explains what's missing.
var chat = builder.AddProject<Projects.BikeBuilder_API_Chat>("chat",
        options => options.ExcludeLaunchProfile = isTest)
    .WithReference(mcp).WaitFor(mcp)
    .WithReference(ollama)
    .WithEnvironment("Auth0__Authority", isTest ? TestOidcIssuer : Auth0Authority)
    .WithEnvironment("Auth0__Audience", isTest ? OidcAudience : Auth0Audience);
WithWebAppOrigins(chat);
if (isTest)
{
  chat.WithHttpEndpoint(port: 18900)
      .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
      .WithEnvironment("Auth0__RequireHttpsMetadata", "false")
      .WithEnvironment("Auth0__RoleClaim", "role")
      .WaitFor(oidc!);
}
chat.WithHttpHealthCheck("/");

// --- API gateway ------------------------------------------------------------------------
// One constant browser-facing origin (dev 7500 / test 18700) that the WASM apps' baked
// wwwroot/appsettings*.json base addresses point at unconditionally. It is served by either
// the real APIM self-hosted gateway container (when the Apim:* user secrets are present -
// see infra/README.md for provisioning and token generation) or the YARP fallback project
// (always available: CI has no Azure credentials and must stay offline-green). Both
// implement the same route contract: /orders, /ratings and /chat prefix-stripped, everything
// else to the catalog api. Both branches name the resource "gateway" so the integration-test
// fixture waits on one name regardless of mode.
var gatewayPort = isTest ? 18700 : 7500;
var apimConfigEndpoint = builder.Configuration["Apim:ConfigEndpoint"];
var apimGatewayToken = builder.Configuration[isTest ? "Apim:GatewayTokenTest" : "Apim:GatewayTokenDev"];

if (!string.IsNullOrWhiteSpace(apimConfigEndpoint) && !string.IsNullOrWhiteSpace(apimGatewayToken))
{
  // Real APIM self-hosted gateway. It pulls the API/policy config from the cloud instance,
  // and the per-gateway policies in infra/modules/apim.bicep rewrite its backends to the
  // host.docker.internal ports for this mode (local-dev vs local-test gateway identity).
  builder.AddContainer("gateway", "mcr.microsoft.com/azure-api-management/gateway", "v2")
      .WithHttpEndpoint(port: gatewayPort, targetPort: 8080)
      .WithEnvironment("config.service.endpoint", apimConfigEndpoint)
      .WithEnvironment("config.service.auth", $"GatewayKey {apimGatewayToken}")
      // No-op on Docker Desktop (the name is built in); makes a native-Linux engine work too.
      .WithContainerRuntimeArgs("--add-host=host.docker.internal:host-gateway")
      // Healthy means the gateway authenticated to the config endpoint and is serving.
      .WithHttpHealthCheck("/status-0123456789abcdef")
      .WithLifetime(lifetime);
}
else
{
  var gatewayFallback = builder.AddProject<Projects.BikeBuilder_Gateway>("gateway",
          options => options.ExcludeLaunchProfile = isTest)
      // The route table lives in the project's appsettings.json; only the destinations are
      // mode-dependent. No WaitFor on the backends - the proxy just 502s until they're up.
      .WithEnvironment("ReverseProxy__Clusters__api__Destinations__default__Address", api.GetEndpoint("http"))
      .WithEnvironment("ReverseProxy__Clusters__orders__Destinations__default__Address", orders.GetEndpoint("http"))
      .WithEnvironment("ReverseProxy__Clusters__ratings__Destinations__default__Address", ratings.GetEndpoint("http"))
      .WithEnvironment("ReverseProxy__Clusters__chat__Destinations__default__Address", chat.GetEndpoint("http"));
  if (isTest)
  {
    gatewayFallback.WithHttpEndpoint(port: gatewayPort)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
  }
  gatewayFallback.WithHttpHealthCheck("/healthz");
}

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

    context.EnvironmentVariables["WebAppOrigins__0"] = webAdmin.GetEndpoint("https");
    context.EnvironmentVariables["WebAppOrigins__1"] = webAdmin.GetEndpoint("http");
  });
}

// Appends the storefront's origins to the same WebAppOrigins array, at indices the helper
// above doesn't use (it writes __0/__1 in dev and only __0 in test).
void WithStorefrontOrigins<T>(IResourceBuilder<T> resource) where T : IResourceWithEnvironment
{
  resource.WithEnvironment(context =>
  {
    if (isTest)
    {
      context.EnvironmentVariables["WebAppOrigins__1"] = TestWebPublicBaseAddress;
      return;
    }

    context.EnvironmentVariables["WebAppOrigins__2"] = webPublic.GetEndpoint("https");
    context.EnvironmentVariables["WebAppOrigins__3"] = webPublic.GetEndpoint("http");
  });
}

// The Functions Worker SDK's `dotnet run` starts whichever `func` is first on PATH. Visual
// Studio puts its own bundled Core Tools (%LOCALAPPDATA%\AzureFunctionsTools) first for
// everything it launches - Test Explorer, F5 - and that copy lags the machine-wide install
// badly: its host can't load the .NET 10 assemblies the notifications worker's Service Bus and
// SignalR extensions need, so the resource died at startup only when the tests ran from VS.
// Prefer the machine-wide install (the winget/MSI location) whenever one exists; elsewhere
// (CI, a machine without it) PATH is left alone.
void WithInstalledCoreTools<T>(IResourceBuilder<T> resource) where T : IResourceWithEnvironment
{
  var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Azure Functions Core Tools");
  if (!Directory.Exists(installed))
    return;

  var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
  resource.WithEnvironment("PATH", installed + Path.PathSeparator + path);
}
