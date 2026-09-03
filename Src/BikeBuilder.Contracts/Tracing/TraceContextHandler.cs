using System.Diagnostics;

namespace BikeBuilder.Contracts.Tracing;

// Starts a client span per outbound request and puts its W3C traceparent on the wire. Used by
// the WASM and MAUI heads (server apps get this from HttpClient's own instrumentation). The
// header is written only when absent: .NET's DiagnosticsHandler already injects it wherever it
// runs, and whether the browser HttpClientHandler does is not something this relies on.
// Registered before the resilience handler, so one span (and one trace id) covers every retry.
public sealed class TraceContextHandler : DelegatingHandler
{
  const string TraceParentHeader = "traceparent";
  const string TraceStateHeader = "tracestate";

  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
  {
    ClientTracing.EnsureListener();
    // Low-cardinality name: gRPC paths are method names and GraphQL is always /graphql.
    using var activity = ClientTracing.Source.StartActivity(
        $"{request.Method} {request.RequestUri?.AbsolutePath}", ActivityKind.Client);

    if (activity is not null && !request.Headers.Contains(TraceParentHeader))
    {
      request.Headers.TryAddWithoutValidation(TraceParentHeader, activity.Id);
      if (activity.TraceStateString is { Length: > 0 } traceState)
        request.Headers.TryAddWithoutValidation(TraceStateHeader, traceState);
    }

    var response = await base.SendAsync(request, cancellationToken);

    activity?.SetTag("http.response.status_code", (int)response.StatusCode);
    if (!response.IsSuccessStatusCode)
      activity?.SetStatus(ActivityStatusCode.Error);

    return response;
  }
}
