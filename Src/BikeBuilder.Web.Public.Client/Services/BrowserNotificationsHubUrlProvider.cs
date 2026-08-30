namespace BikeBuilder.Web.Public.Services;

// In WebAssembly the hub lives on the same origin the page was served from.
public class BrowserNotificationsHubUrlProvider(NavigationManager _navigation) : INotificationsHubUrlProvider
{
  public Uri GetHubUri() => new(new Uri(_navigation.BaseUri), "/hubs/notifications");
}
