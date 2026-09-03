using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.Hosting;

public static class TraceIdResponseHeader
{
  // Duplicated from BikeBuilder.Contracts.Tracing.TraceHeaders: ServiceDefaults has no
  // project references by design.
  public const string HeaderName = "X-Trace-Id";

  // Hands the request's W3C trace id back to the caller on every response - the one id that
  // finds the request across every service, message and database call it touched. OnStarting
  // survives the exception handler's Response.Clear(), so error responses carry it too.
  public static IApplicationBuilder UseTraceIdResponseHeader(this IApplicationBuilder app) =>
      app.Use((context, next) =>
      {
        context.Response.OnStarting(static state =>
        {
          var httpContext = (HttpContext)state;
          if (Activity.Current is { } activity)
            httpContext.Response.Headers[HeaderName] = activity.TraceId.ToHexString();
          return Task.CompletedTask;
        }, context);
        return next(context);
      });
}
