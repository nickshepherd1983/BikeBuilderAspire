using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;

namespace BikeBuilder.API.Notifications.Functions;

/// <summary>
/// Sends the order receipt. Consumes the dedicated order-emails queue rather than the
/// notifications one: Basic-tier Service Bus has no topics, and a second receiver on the
/// notifications queue would compete with the SignalR fan-out for the same messages.
/// Locally the AppHost points the sender at the smtp4dev container; deployed, at Mailjet.
/// </summary>
public class OrderEmailFunctions(IEmailSender emailSender, ILogger<OrderEmailFunctions> logger)
{
  [Function("SendOrderConfirmationEmail")]
  public async Task SendOrderConfirmationEmail(
      [ServiceBusTrigger(ServiceBusQueueNames.OrderEmails, Connection = "servicebus")] ServiceBusReceivedMessage message,
      CancellationToken cancellationToken)
  {
    var messageType = message.ApplicationProperties.GetValueOrDefault("MessageType") as string;
    using var scope = MessageScope.Begin(logger, message, messageType);

    if (messageType != ServiceBusMessageTypes.OrderConfirmationRequested)
    {
      // Not a transient condition, so returning (which completes the message) beats letting
      // it bounce through ten redeliveries into the dead-letter queue.
      logger.LogWarning("Ignoring message of type {MessageType} on {Queue}", messageType, ServiceBusQueueNames.OrderEmails);
      return;
    }

    var order = message.Body.ToObjectFromJson<OrderConfirmationRequestedEvent>()!;

    // Inside the checkout's trace (see MessageScope), so the dashboard shows click -> order ->
    // queue -> this send -> SMTP/Mailjet as one story.
    var hasProducer = MessageTraceContext.TryGetProducerContext(message.ApplicationProperties, out var producer);
    using var activity = MessageScope.StartConsumerActivity(Tracing.Source, "Send order confirmation email", hasProducer, producer);
    activity?.SetTag("bikebuilder.order_id", order.OrderId);

    // Exceptions propagate on purpose: the host abandons the message, Service Bus redelivers
    // it up to the queue's maxDeliveryCount and then dead-letters it.
    await emailSender.SendAsync(OrderConfirmationEmailBuilder.Build(order), cancellationToken);
  }
}
