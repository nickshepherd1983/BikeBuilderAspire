using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Shared Aspire service defaults: OpenTelemetry (traces, metrics, logs), health checks,
// service discovery, and resilient HttpClient defaults. Apps call AddServiceDefaults from
// Program.cs; the OTLP exporter activates when the AppHost injects OTEL_EXPORTER_OTLP_ENDPOINT
// (pointing at the Aspire dashboard), so a standalone run simply exports nothing.
//
// Deliberately NOT here: the "Azure.Experimental.EnableActivitySource" AppContext switch each
// app sets as its first statement - it must run before any Azure SDK client is constructed,
// and hiding it in a library call would make that ordering fragile.
public static class Extensions
{
  const string HealthEndpointPath = "/health";
  const string AlivenessEndpointPath = "/alive";

  /// <param name="includeAspNetCoreTracing">
  /// False for the Functions worker: the Functions host already emits the request/invocation
  /// span, and worker-side AspNetCore instrumentation would double-report every request.
  /// </param>
  /// <param name="aspNetCoreTraceFilter">Optional per-request trace filter (e.g. drop health probes).</param>
  /// <param name="configureTracing">Adds app-specific ActivitySources on top of the shared ones.</param>
  public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder,
      bool includeAspNetCoreTracing = true,
      Func<HttpContext, bool>? aspNetCoreTraceFilter = null,
      Action<TracerProviderBuilder>? configureTracing = null)
      where TBuilder : IHostApplicationBuilder
  {
    builder.ConfigureOpenTelemetry(includeAspNetCoreTracing, aspNetCoreTraceFilter, configureTracing);

    builder.AddDefaultHealthChecks();

    builder.Services.AddServiceDiscovery();

    builder.Services.ConfigureHttpClientDefaults(http =>
    {
      // Retries, circuit breaker, and per-attempt/total timeouts for every factory-built client.
      // Unsafe methods (POST/PUT/PATCH/DELETE) are excluded from the retry: a replayed request
      // that already executed would create a duplicate user, upload, or cart line. Clients whose
      // POSTs are safe to retry (the GraphQL orders client, token fetches) opt back in
      // explicitly; gRPC-Web calls get their retry from the channel's ServiceConfig instead.
      http.AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
      http.AddServiceDiscovery();
    });

    return builder;
  }

  public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder,
      bool includeAspNetCoreTracing = true,
      Func<HttpContext, bool>? aspNetCoreTraceFilter = null,
      Action<TracerProviderBuilder>? configureTracing = null)
      where TBuilder : IHostApplicationBuilder
  {
    builder.Logging.AddOpenTelemetry(logging =>
    {
      logging.IncludeFormattedMessage = true;
      logging.IncludeScopes = true;
    });

    builder.Services.AddOpenTelemetry()
        // No ConfigureResource: the AppHost injects OTEL_SERVICE_NAME (the resource name),
        // which the SDK picks up automatically.
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            // Retry / circuit-breaker / timeout events from every Polly pipeline (the HTTP
            // resilience handler and the app-defined Cosmos/Redis pipelines), so transient
            // faults are visible in the dashboard rather than silently absorbed.
            .AddMeter("Polly"))
        .WithTracing(tracing =>
        {
          if (includeAspNetCoreTracing)
          {
            tracing.AddAspNetCoreInstrumentation(options =>
            {
              if (aspNetCoreTraceFilter is not null)
                options.Filter = context => aspNetCoreTraceFilter(context);
            });
          }

          tracing
              .AddHttpClientInstrumentation()
              // Blob Storage + Service Bus (+ any future Azure SDK client) - every server
              // app talks to at least one Azure service.
              .AddSource("Azure.*");

          configureTracing?.Invoke(tracing);
        });

    builder.AddOpenTelemetryExporters();

    return builder;
  }

  static void AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
      where TBuilder : IHostApplicationBuilder
  {
    var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    if (useOtlpExporter)
      builder.Services.AddOpenTelemetry().UseOtlpExporter();
  }

  public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
      where TBuilder : IHostApplicationBuilder
  {
    // A default liveness check to ensure the app is responsive.
    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

    return builder;
  }

  public static WebApplication MapDefaultEndpoints(this WebApplication app)
  {
    // Development only: health endpoints can leak internals and, unsecured, invite DoS in
    // production. See https://aka.ms/dotnet/aspire/healthchecks before enabling them wider.
    if (app.Environment.IsDevelopment())
    {
      // All health checks must pass for the app to be considered ready for traffic.
      app.MapHealthChecks(HealthEndpointPath);

      // Only checks tagged "live" must pass for the app to be considered alive.
      app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
      {
        Predicate = r => r.Tags.Contains("live")
      });
    }

    return app;
  }
}
