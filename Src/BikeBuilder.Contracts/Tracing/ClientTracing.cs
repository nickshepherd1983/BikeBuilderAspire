using System.Diagnostics;

namespace BikeBuilder.Contracts.Tracing;

// Trace origination for the heads that run no OpenTelemetry SDK: the WASM apps and the MAUI
// app. Without a listener ActivitySource.StartActivity returns null, so this registers one for
// the process lifetime; the activities it produces are never exported anywhere - their whole
// job is to mint the traceparent that TraceContextHandler sends, so the server-side trace
// starts with the user's action rather than at the gateway.
public static class ClientTracing
{
  public const string SourceName = "BikeBuilder.Client";

  public static readonly ActivitySource Source = new(SourceName);

  // Kept alive on purpose (S2930): disposing it would silently stop every client trace.
  static readonly ActivityListener _listener = new()
  {
    ShouldListenTo = source => source.Name == SourceName,
    // Recorded => trace-flags 01. With 00 every server's parent-based sampler would drop the
    // request, and nothing would be correlated at all.
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
  };

  static ClientTracing() => ActivitySource.AddActivityListener(_listener);

  // Touching the type runs the static constructor; callers use this to make that explicit.
  public static void EnsureListener()
  {
    // Intentionally empty - see the comment above.
  }

  public static string? CurrentTraceId => Activity.Current?.TraceId.ToHexString();
}
