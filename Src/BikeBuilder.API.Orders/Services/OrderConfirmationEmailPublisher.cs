namespace BikeBuilder.API.Orders.Services;

// Queues the order receipt for BikeBuilder.API.Notifications to send. A dedicated type rather
// than a second IEventPublisher registration: HotChocolate's [Service] injection resolves by
// type, and the Notifications-queue publisher already owns that interface. Reuses
// ServiceBusEventPublisher for the wire format (PascalCase JSON + MessageType property).
//
// Best effort by design: the order is already committed when this runs, so a Service Bus
// hiccup here is logged and swallowed rather than shown to the shopper as a failed checkout.
public sealed class OrderConfirmationEmailPublisher(ServiceBusClient client, ILogger<OrderConfirmationEmailPublisher> logger)
{
  readonly IEventPublisher _publisher = new ServiceBusEventPublisher(client.CreateSender(ServiceBusQueueNames.OrderEmails));

  public async Task TryPublishAsync(OrderConfirmationRequestedEvent request, CancellationToken cancellationToken)
  {
    try
    {
      await _publisher.PublishAsync(ServiceBusMessageTypes.OrderConfirmationRequested, request, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Could not queue the confirmation email for order {OrderId}", request.OrderId);
    }
  }
}
