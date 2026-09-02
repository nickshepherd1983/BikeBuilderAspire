using System.Net.Http.Headers;

namespace BikeBuilder.MCP.Services;

// Copies the MCP caller's bearer token onto an outgoing request, so a downstream service
// applies its own role checks to the actual user (the orders service's ViewOrders queries).
// Tool invocations run inside the HTTP request that carried them, which is what makes the
// accessor's ambient context the right one here.
public class BearerForwardingHandler(IHttpContextAccessor _httpContextAccessor) : DelegatingHandler
{
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var authorization = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
    if (!string.IsNullOrEmpty(authorization) && AuthenticationHeaderValue.TryParse(authorization, out var header))
      request.Headers.Authorization = header;

    return base.SendAsync(request, cancellationToken);
  }
}
