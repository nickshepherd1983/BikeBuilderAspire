using BikeBuilder.API.Chat.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using OllamaSharp;
using OpenTelemetry.Trace;

// The admin app's assistant backend: runs a tool-calling loop against a locally hosted Ollama
// model, with the tools coming from the BikeBuilder.MCP server. Lives server-side because the
// browser-hosted admin app can neither reach the other services' internal endpoints nor keep
// the loop's state; the model itself runs on the developer machine, not in a container.
var builder = WebApplication.CreateBuilder(args);

// "/" is the health probe. The Microsoft.Extensions.AI source carries the GenAI spans (model
// calls, tool invocations) so a chat request reads as one trace in the dashboard.
builder.AddServiceDefaults(
    aspNetCoreTraceFilter: context => context.Request.Path != "/",
    configureTracing: tracing => tracing.AddSource("Experimental.Microsoft.Extensions.AI"));

var ollamaOptions = OllamaOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(ollamaOptions);

// Model completions and MCP tool calls can run for a minute or more on a local model, far past
// the standard resilience handler's 10s attempt timeout, so these two clients drop it and rely
// on their own timeouts instead. EXTEXP0001: RemoveAllResilienceHandlers is still marked
// experimental, but it's the documented way to opt a client out of the shared default.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient(OllamaOptions.HttpClientName, client =>
    {
      client.BaseAddress = ollamaOptions.Endpoint;
      client.Timeout = TimeSpan.FromMinutes(5);
    })
    .RemoveAllResilienceHandlers();
builder.Services.AddHttpClient(McpToolsFactory.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(2))
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

// OllamaSharp's client is both the raw Ollama API (status checks) and an IChatClient; the
// pipeline built on it runs the function-calling loop and emits the OpenTelemetry spans.
builder.Services.AddSingleton(sp => new OllamaApiClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(OllamaOptions.HttpClientName),
    ollamaOptions.Model));
builder.Services.AddSingleton<IChatClient>(sp =>
    ((IChatClient)sp.GetRequiredService<OllamaApiClient>()).AsBuilder()
        .UseFunctionInvocation(configure: client =>
        {
          // A question rarely needs more than three or four lookups; this bounds a model that
          // keeps calling tools without converging.
          client.MaximumIterationsPerRequest = 8;
          // Tool failures (a missing role, an unknown id) are worded for the model to relay.
          client.IncludeDetailedErrors = true;
        })
        .UseOpenTelemetry(configure: options => options.EnableSensitiveData = builder.Environment.IsDevelopment())
        .Build(sp));
builder.Services.AddSingleton<McpToolsFactory>();
builder.Services.AddScoped<ChatService>();

// The signed-in admin app calls these endpoints straight from the browser (through the gateway).
var webAppOrigins = builder.Configuration.GetSection("WebAppOrigins").Get<string[]>()
    ?? ["https://localhost:7200", "http://localhost:7201"];
builder.Services.AddCors(options => options.AddPolicy("BlazorWasmClient", policy =>
    policy.WithOrigins(webAppOrigins).AllowAnyMethod().AllowAnyHeader()));

// The same JWT validation and policy set as BikeBuilder.API - see AuthorizationConstants.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
      options.Authority = builder.Configuration["Auth0:Authority"]
          ?? throw new InvalidOperationException("Auth0:Authority is not configured.");
      options.Audience = builder.Configuration["Auth0:Audience"];
      // False only in the integration-test environment, where the stub OIDC issuer is plain http.
      options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth0:RequireHttpsMetadata", true);
      options.MapInboundClaims = false;
      options.TokenValidationParameters.NameClaimType = "sub";
      options.TokenValidationParameters.RoleClaimType = RoleClaim.Resolve(builder.Configuration[RoleClaim.ConfigKey]);
    });
builder.Services.AddAuthorization(options =>
{
  foreach (var (name, allowedRoles) in Policies.All)
    options.AddPolicy(name, policy => policy.RequireRole(allowedRoles));
});
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("BlazorWasmClient");
app.UseAuthentication();
app.UseAuthorization();

app.MapChatEndpoints();
// Stays anonymous and never touches Ollama - the AppHost uses it as the health probe, and CI
// runs the whole topology with no model installed.
app.MapGet("/", () => "BikeBuilder.API.Chat — assistant endpoints at /api/chat.");
app.MapDefaultEndpoints();

await app.RunAsync();
