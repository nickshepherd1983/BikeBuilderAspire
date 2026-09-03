using System.Diagnostics;

namespace BikeBuilder.Contracts.Messaging;

// Recovers the producer's trace context from a Service Bus message. The Azure SDK stamps the
// sender's traceparent on every message as the "Diagnostic-Id" application property (behind the
// Azure.Experimental.EnableActivitySource switch each publishing app sets); the SDK's own
// processing span only LINKS to it, so consumers that want their work to appear inside the
// originating trace parent their spans on this context explicitly. Takes the raw dictionary so
// Contracts stays free of Azure package references.
public static class MessageTraceContext
{
  public const string DiagnosticIdProperty = "Diagnostic-Id";

  public static bool TryGetProducerContext(IReadOnlyDictionary<string, object> applicationProperties, out ActivityContext context)
  {
    if (applicationProperties.TryGetValue(DiagnosticIdProperty, out var value) && value is string traceparent)
      return ActivityContext.TryParse(traceparent, null, isRemote: true, out context);

    context = default;
    return false;
  }
}
