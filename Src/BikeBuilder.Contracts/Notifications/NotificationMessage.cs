namespace BikeBuilder.Contracts.Notifications;

// What the notification hub broadcasts (both the self-hosted hub in Web.Public and Azure SignalR
// Service via the Notifications Function). Text is the finished toast; TraceId is the trace of
// the request that raised the event - the same id the shopper or admin saw on their own
// response - so a toast can be followed back to the order or rating behind it.
public sealed record NotificationMessage(string Text, string MessageType, string? TraceId);
