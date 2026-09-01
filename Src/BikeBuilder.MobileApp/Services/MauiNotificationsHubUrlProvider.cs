namespace BikeBuilder.MobileApp;

// The web clients derive the hub URL from the page origin; a native app has no origin, so
// the address is configured. CORS on the hub doesn't apply here - it's browser enforcement,
// and this HubConnection is a plain HTTP/WebSocket client.
public class MauiNotificationsHubUrlProvider : INotificationsHubUrlProvider
{
  public Uri GetHubUri() => AppEnvironment.NotificationsHubUri;
}
