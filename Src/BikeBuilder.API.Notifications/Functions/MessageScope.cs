using Azure.Messaging.ServiceBus;

namespace BikeBuilder.API.Notifications.Functions;

// Shared by the two Service Bus-triggered functions: the log scope that stamps a message's ids
// on every log line, and the consumer span that puts the function's work inside the
// PRODUCER's trace. The Functions host's own trigger span only links to the producer (Azure
// SDK messaging convention), so without this a checkout and its receipt are two traces.
static class MessageScope
{
  public static IDisposable? Begin(ILogger logger, ServiceBusReceivedMessage message, string? messageType) =>
      logger.BeginScope(new Dictionary<string, object?>
      {
        ["MessageId"] = message.MessageId,
        ["CorrelationId"] = message.CorrelationId,
        ["MessageType"] = messageType
      });

  // Parented on the producer context when the message carries one, with the host's ambient
  // trigger span kept as a link; a plain child span otherwise.
  public static Activity? StartConsumerActivity(ActivitySource source, string name, bool hasProducer, ActivityContext producer)
  {
    var triggerSpan = Activity.Current;
    if (!hasProducer)
      return source.StartActivity(name);

    triggerSpan?.SetTag("bikebuilder.producer_trace_id", producer.TraceId.ToHexString());
    ActivityLink[]? links = triggerSpan is null ? null : [new ActivityLink(triggerSpan.Context)];
    return source.StartActivity(name, ActivityKind.Consumer, producer, links: links);
  }
}

static class Tracing
{
  public static readonly ActivitySource Source = new("BikeBuilder.API.Notifications");
}
