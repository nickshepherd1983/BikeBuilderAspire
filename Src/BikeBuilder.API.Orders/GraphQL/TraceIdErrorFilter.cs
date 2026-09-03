using System.Diagnostics;
using BikeBuilder.Contracts.Tracing;
using HotChocolate.Execution;

namespace BikeBuilder.API.Orders.GraphQL;

// Every GraphQL error (a declined card, an expired cart, an unexpected exception) carries the
// request's trace id as the "traceId" extension, so the storefront can show it as "(ref <id>)"
// and support can find the request in the dashboard from a screenshot.
public sealed class TraceIdErrorFilter : IErrorFilter
{
  public IError OnError(IError error) =>
      Activity.Current is { } activity
          ? error.SetExtension(TraceHeaders.GraphQLExtension, activity.TraceId.ToHexString())
          : error;
}
