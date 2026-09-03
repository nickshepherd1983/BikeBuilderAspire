using System.Diagnostics;
using BikeBuilder.Contracts.Tracing;
using Grpc.Core.Interceptors;

namespace BikeBuilder.API.Services;

// Hands the request's trace id back as a response trailer - the gRPC equivalent of the
// X-Trace-Id header the HTTP endpoints set. Added before the handler runs so it rides both the
// success trailers and an RpcException's (Grpc.AspNetCore merges ResponseTrailers into the
// error response). The WASM app appends it to error toasts as "(ref <id>)".
public sealed class TraceIdServerInterceptor : Interceptor
{
  public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context,
      UnaryServerMethod<TRequest, TResponse> continuation)
  {
    if (Activity.Current is { } activity)
      context.ResponseTrailers.Add(TraceHeaders.GrpcTrailer, activity.TraceId.ToHexString());

    return continuation(request, context);
  }
}
