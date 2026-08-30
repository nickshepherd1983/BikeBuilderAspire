using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace BikeBuilder.Web.Public.Services;

// Server-circuit implementation: component code runs inside this process, and the
// externally visible host:port from the original request (e.g. a Docker host-port mapping
// or a reverse proxy's public address) may not be reachable from in here - so connect
// using the address Kestrel is actually bound to.
public class ServerNotificationsHubUrlProvider(IServer _server) : INotificationsHubUrlProvider
{
  public Uri GetHubUri()
  {
    var address = _server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
        ?? throw new InvalidOperationException("Could not determine the server's own listening address.");

    var normalized = address
        .Replace("://+:", "://localhost:")
        .Replace("://*:", "://localhost:")
        .Replace("://[::]:", "://localhost:")
        .Replace("://0.0.0.0:", "://localhost:");

    return new Uri(new Uri(normalized), "/hubs/notifications");
  }
}
