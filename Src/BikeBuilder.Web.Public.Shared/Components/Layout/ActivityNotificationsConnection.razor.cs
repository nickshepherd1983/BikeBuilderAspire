using BikeBuilder.Contracts.Notifications;
using BikeBuilder.Contracts.Resilience;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace BikeBuilder.Web.Public.Components.Layout;

public partial class ActivityNotificationsConnection(
    ISnackbar _snackbar,
    INotificationsHubUrlProvider _hubUrl,
    ILogger<ActivityNotificationsConnection> _logger) : IAsyncDisposable
{
  HubConnection? _hubConnection;
  CancellationTokenSource? _connectCts;

  protected override void OnInitialized()
  {
    // Skip the static prerender pass (its connection would be discarded immediately). Once
    // interactive - server circuit or WebAssembly - the injected provider knows the hub
    // address for that runtime: the page's own origin in the browser, Kestrel's actual
    // bound address on the circuit (where the request-derived origin may not be reachable
    // from inside the process).
    if (!RendererInfo.IsInteractive)
      return;

    _hubConnection = new HubConnectionBuilder()
        .WithUrl(_hubUrl.GetHubUri())
        .WithAutomaticReconnect()
        .Build();

    // One feed on "ReceiveNotification", already formatted by Web.Public's Service Bus
    // listener: components and bike builds as they're created, plus ratings and orders.
    _hubConnection.On<NotificationMessage>("ReceiveNotification",
        message => InvokeAsync(() => ShowToast(message, Severity.Info)));

    // Fire-and-forget with retries: these toasts are a nicety, and this connection now lives
    // in the layout - an exception awaited here would take down every page, not just one.
    // Automatic reconnect only covers drops AFTER a successful start, hence the retry pipeline.
    _connectCts = new CancellationTokenSource();
    _ = ConnectWithRetriesAsync(_hubConnection, _connectCts.Token);
  }

  // The toast text is unchanged (the integration tests match on it); the originating request's
  // trace id rides along as a hover title and a log line, so a toast can be traced back to the
  // order or rating that caused it.
  void ShowToast(NotificationMessage message, Severity severity)
  {
    _logger.LogInformation("Toast {MessageType} received (trace {TraceId})", message.MessageType, message.TraceId);
    _snackbar.Add(builder =>
    {
      builder.OpenElement(0, "span");
      if (message.TraceId is not null)
        builder.AddAttribute(1, "title", $"trace {message.TraceId}");
      builder.AddContent(2, message.Text);
      builder.CloseElement();
    }, severity, key: message.Text);
  }

  static async Task ConnectWithRetriesAsync(HubConnection hubConnection, CancellationToken cancellationToken)
  {
    try
    {
      await HubConnectionRetry.Pipeline.ExecuteAsync(
          static (hub, ct) => new ValueTask(hub.StartAsync(ct)), hubConnection, cancellationToken);
    }
    catch (Exception)
    {
      // Out of attempts, or disposed mid-connect - give up quietly; the storefront works fine
      // without toasts.
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (_connectCts is not null)
    {
      await _connectCts.CancelAsync();
      _connectCts.Dispose();
    }

    if (_hubConnection is not null)
      await _hubConnection.DisposeAsync();
  }
}
