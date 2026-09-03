using Azure.Messaging.ServiceBus;
using BikeBuilder.Contracts.Notifications;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace BikeBuilder.API.Notifications.Functions;

/// <summary>
/// Fans Service Bus notification events out to browsers over Azure SignalR Service.
///
/// This used to be a BackgroundService inside the storefront, holding both a Service Bus
/// processor and a self-hosted SignalR hub open. That needs a process running around the
/// clock, which is exactly what scale-to-zero forbids - and an always-on container is what
/// pushes this deployment off the Container Apps free grant. As a Service Bus-triggered
/// Function writing to SignalR in Serverless mode, nothing has to stay awake: the service
/// holds the client connections, and the Function is billed only per message.
///
/// Locally this function is disabled (the AppHost sets AzureWebJobs.BroadcastNotification.Disabled)
/// and Web.Public's own listener does the same job against its self-hosted hub; the two keep
/// the same toast text and the same envelope.
/// </summary>
public class NotificationFunctions(ILogger<NotificationFunctions> logger)
{
  public const string HubName = "notifications";

  /// <summary>
  /// Hands a browser the URL and access token it needs to connect to SignalR Service.
  /// Anonymous by design: the public activity feed is anonymous, exactly as the storefront's
  /// own hub was.
  /// </summary>
  [Function("negotiate")]
  public static string Negotiate(
      [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "notifications/negotiate")] HttpRequestData request,
      [SignalRConnectionInfoInput(HubName = HubName)] string connectionInfo) => connectionInfo;

  [Function("BroadcastNotification")]
  [SignalROutput(HubName = HubName)]
  public SignalRMessageAction[] BroadcastNotification(
      [ServiceBusTrigger(ServiceBusQueueNames.Notifications, Connection = "servicebus")] ServiceBusReceivedMessage message)
  {
    var messageType = message.ApplicationProperties.GetValueOrDefault("MessageType") as string;
    using var scope = MessageScope.Begin(logger, message, messageType);
    var hasProducer = MessageTraceContext.TryGetProducerContext(message.ApplicationProperties, out var producer);

    var text = messageType switch
    {
      ServiceBusMessageTypes.ComponentCreated =>
          $"New component added: {message.Body.ToObjectFromJson<ComponentCreatedEvent>()!.Name}",
      ServiceBusMessageTypes.BikeBuildCreated =>
          $"New bike build created: {message.Body.ToObjectFromJson<BikeBuildCreatedEvent>()!.Name}",
      ServiceBusMessageTypes.RatingCreated =>
          FormatRatingCreated(message.Body.ToObjectFromJson<RatingCreatedEvent>()!),
      ServiceBusMessageTypes.OrderPlaced =>
          FormatOrderPlaced(message.Body.ToObjectFromJson<OrderPlacedEvent>()!),
      _ => null
    };

    if (text is null)
      return [];

    // The SignalR output binding produces no span of its own; this one sits inside the
    // producer's trace (see MessageScope) so the fan-out shows up under the checkout.
    using var activity = MessageScope.StartConsumerActivity(Tracing.Source, "SignalR Service broadcast", hasProducer, producer);
    activity?.SetTag("bikebuilder.message_type", messageType);

    var traceId = hasProducer ? producer.TraceId.ToHexString() : activity?.TraceId.ToHexString();
    var envelope = new NotificationMessage(text, messageType!, traceId);
    var broadcast = new SignalRMessageAction("ReceiveNotification", [envelope]);

    // Order events additionally go out on a dedicated method so clients that only care
    // about orders (the authenticated WASM app) don't have to string-match the feed.
    return messageType == ServiceBusMessageTypes.OrderPlaced
        ? [broadcast, new SignalRMessageAction("ReceiveOrderNotification", [envelope])]
        : [broadcast];
  }

  static string FormatRatingCreated(RatingCreatedEvent rating) =>
      $"New {rating.Stars}-star rating for {rating.BikeBuildName}";

  // Invariant "$" formatting keeps the toast text machine-independent (the integration
  // test asserts on it).
  static string FormatOrderPlaced(OrderPlacedEvent order) =>
      $"New order placed by {order.CustomerName}: {order.ItemCount} item(s), ${order.Total.ToString("0.00", CultureInfo.InvariantCulture)}";
}
