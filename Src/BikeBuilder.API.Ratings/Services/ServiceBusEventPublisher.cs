using System.Diagnostics;

namespace BikeBuilder.API.Ratings.Services;

// Mirrors BikeBuilder.API's ServiceBusEventPublisher exactly - change all three copies
// together. PascalCase payload (default JsonSerializer options) and the MessageType application
// property the consumers switch on. Kept as a copy rather than shared via Contracts so
// Contracts stays free of Azure package references.
public class ServiceBusEventPublisher(ServiceBusSender sender) : IEventPublisher
{
  public async Task PublishAsync<TEvent>(string messageType, TEvent payload, CancellationToken cancellationToken) where TEvent : class
  {
    var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(payload))
    {
      ContentType = "application/json",
      // A consumer-side idempotency handle and log-scope key.
      MessageId = Guid.NewGuid().ToString("N"),
      // The W3C trace id of the request that raised the event - the same id its caller got
      // back in X-Trace-Id. The SDK additionally stamps the full traceparent as Diagnostic-Id.
      CorrelationId = Activity.Current?.TraceId.ToHexString(),
      Subject = messageType
    };
    message.ApplicationProperties["MessageType"] = messageType;

    await sender.SendMessageAsync(message, cancellationToken);
  }
}
