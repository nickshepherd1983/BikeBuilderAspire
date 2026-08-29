using BikeBuilder.API.Ratings;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Azure SDK messaging tracing (Service Bus send spans + traceparent stamping on the
// RatingCreated events) is still behind this experimental switch. Must be set before any
// ServiceBusClient is constructed.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

// Deliberately no AspNetCore instrumentation: the Functions host already emits the
// request/invocation span and adding it in the worker double-reports every request.
builder.AddServiceDefaults(includeAspNetCoreTracing: false);
// Correlates worker spans with the Functions host's invocation spans. A second
// AddOpenTelemetry() call composes with the one inside AddServiceDefaults.
builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

builder.UseMiddleware<CorsMiddleware>();
builder.UseWhen<JwtAuthenticationMiddleware>(context => context.FunctionDefinition.Name == "CreateRating");

// The "cosmos" connection string is injected by the AppHost (WithReference); the db and
// container themselves are provisioned by the AppHost's Cosmos resource model.
builder.AddAzureCosmosClient("cosmos", configureClientOptions: options =>
{
  // Gateway + LimitToEndpoint: the emulator advertises "localhost" as its endpoint, so
  // the SDK must stick to the address we gave it.
  options.ConnectionMode = ConnectionMode.Gateway;
  options.LimitToEndpoint = true;
  // camelCase documents, so the stored JSON matches the REST API's shape ("id",
  // "bikeBuildId", ...) and the /bikeBuildId partition key path.
  options.UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
  // Explicit opt-in: emits "Azure.Cosmos.Operation" activities for container operations
  // (the default has flip-flopped between SDK builds, so be deterministic).
  options.CosmosClientTelemetryOptions = new CosmosClientTelemetryOptions { DisableDistributedTracing = false };
  // Only needed if the Cosmos emulator runs https with a self-signed cert (the Aspire-run
  // vNext emulator defaults to plain http); never set in real Azure.
  options.HttpClientFactory = builder.Configuration.GetValue("Cosmos:DisableServerCertificateValidation", false)
      ? () => new HttpClient(new HttpClientHandler
      {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
      })
      : null;
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<CosmosClient>().GetContainer("bikebuilder", "ratings"));

builder.AddAzureServiceBusClient("servicebus");
builder.Services.AddSingleton(sp => sp.GetRequiredService<ServiceBusClient>().CreateSender(ServiceBusQueueNames.Notifications));
builder.Services.AddSingleton<IEventPublisher, ServiceBusEventPublisher>();

var app = builder.Build();

await app.RunAsync();
