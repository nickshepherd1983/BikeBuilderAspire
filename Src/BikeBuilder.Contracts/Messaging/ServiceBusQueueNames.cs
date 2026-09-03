namespace BikeBuilder.Contracts.Messaging;

public static class ServiceBusQueueNames
{
  public const string Notifications = "bikebuilder-notifications";
  // Its own queue rather than a second consumer on Notifications: the deployed namespace is
  // Basic tier (queues only, no topics), and two receivers on one queue compete for messages -
  // the email sender would swallow toasts and the SignalR fan-out would swallow receipts.
  public const string OrderEmails = "bikebuilder-order-emails";
}
