using Grpc.Core;

namespace BikeBuilder.Contracts.Tracing;

// Appends the trace id to a user-facing error message as "(ref <id>)", so what a user reads
// off a failed toast is exactly what finds the request in the dashboard. The server's id is
// preferred (trailer / header) - in WASM the client span is already disposed by the time a
// catch block runs, so the ambient id is only a fallback for the Blazor server circuit.
public static class ErrorReference
{
  public static string Format(string message, string? traceId) =>
      string.IsNullOrEmpty(traceId) ? message : $"{message} (ref {traceId})";

  public static string From(RpcException exception) =>
      Format(exception.Status.Detail, exception.Trailers.GetValue(TraceHeaders.GrpcTrailer) ?? ClientTracing.CurrentTraceId);

  public static string From(HttpResponseMessage response, string message)
  {
    var traceId = response.Headers.TryGetValues(TraceHeaders.ResponseHeader, out var values)
        ? values.FirstOrDefault()
        : ClientTracing.CurrentTraceId;
    return Format(message, traceId);
  }
}
