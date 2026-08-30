namespace BikeBuilder.Web.Public.Services;

// Where the live-activity page should connect for /hubs/notifications. The answer differs
// per runtime: in the browser it's simply the page's own origin, but on the server circuit
// the request-derived origin may not be reachable from inside the process (Docker host-port
// mappings, reverse proxies), so the host supplies its own implementation over the address
// Kestrel is actually bound to.
public interface INotificationsHubUrlProvider
{
  Uri GetHubUri();
}
