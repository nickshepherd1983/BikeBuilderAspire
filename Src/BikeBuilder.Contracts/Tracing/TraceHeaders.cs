namespace BikeBuilder.Contracts.Tracing;

// Where the W3C trace id - the system's one correlation id - is handed back to callers. The
// same 32-hex value appears on every span, log record and Service Bus message the request
// touched, so any of these pastes straight into the dashboard's trace search.
public static class TraceHeaders
{
  // Every HTTP response from every service (set by ServiceDefaults' UseTraceIdResponseHeader).
  public const string ResponseHeader = "X-Trace-Id";
  // gRPC response trailer (metadata keys must be lowercase).
  public const string GrpcTrailer = "trace-id";
  // GraphQL error extension; the same name ASP.NET Core's ProblemDetails uses.
  public const string GraphQLExtension = "traceId";
}
