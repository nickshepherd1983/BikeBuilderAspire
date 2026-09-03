using BikeBuilder.Contracts.Tracing;
using StrawberryShake;

namespace BikeBuilder.Web.Public.Services;

// GraphQL errors carry the server's trace id as the "traceId" extension (see the orders
// service's TraceIdErrorFilter); this turns one into the message a toast shows.
public static class ClientErrors
{
  public static string ToUserMessage(this IClientError error) =>
      ErrorReference.Format(error.Message,
          error.Extensions is not null && error.Extensions.TryGetValue(TraceHeaders.GraphQLExtension, out var traceId)
              ? traceId?.ToString()
              : null);
}
