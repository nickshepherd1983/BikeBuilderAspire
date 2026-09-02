using Polly;
using Polly.Retry;

namespace BikeBuilder.Contracts.Resilience;

/// <summary>
/// Retry pipeline for the initial <c>HubConnection.StartAsync</c>: SignalR's automatic reconnect
/// only covers drops after a successful start, so a hub that isn't reachable yet (Web.Public
/// still booting, a cold Container App) needs this on top. Shared by the storefront's activity
/// feed and the back office's order toasts.
/// </summary>
public static class HubConnectionRetry
{
  // Linear: 3s, 6s, 9s, 12s - the same spacing the two hand-rolled loops used to have. Cancelled
  // starts (page disposed mid-connect) propagate rather than being retried.
  public static ResiliencePipeline Pipeline { get; } = new ResiliencePipelineBuilder()
      .AddRetry(new RetryStrategyOptions
      {
        MaxRetryAttempts = 4,
        Delay = TimeSpan.FromSeconds(3),
        BackoffType = DelayBackoffType.Linear,
        ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException)
      })
      .Build();
}
