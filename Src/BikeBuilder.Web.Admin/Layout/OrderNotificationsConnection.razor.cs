using BikeBuilder.Contracts.Notifications;
using BikeBuilder.Contracts.Resilience;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BikeBuilder.Web.Admin.Layout;

public partial class OrderNotificationsConnection(
    ISnackbar _snackbar,
    IConfiguration _configuration,
    ILogger<OrderNotificationsConnection> _logger) : IAsyncDisposable
{
  HubConnection? _hubConnection;
  CancellationTokenSource? _connectCts;

  protected override void OnInitialized()
  {
    var webPublicBaseAddress = _configuration["WebPublicBaseAddress"] ?? "https://localhost:7300";

    // Web.Public's listener rebroadcasts OrderPlaced events on a dedicated hub method, so
    // this client sees only order notifications, not the whole public activity feed.
    _hubConnection = new HubConnectionBuilder()
        .WithUrl(new Uri(new Uri(webPublicBaseAddress), "/hubs/notifications"))
        .WithAutomaticReconnect()
        .Build();

    _hubConnection.On<NotificationMessage>("ReceiveOrderNotification", ShowToast);

    // Fire-and-forget with retries: order toasts are a nicety, and an unreachable
    // Web.Public must never take the app down (an exception awaited here would). Automatic
    // reconnect only covers drops AFTER a successful start, hence the retry pipeline.
    _connectCts = new CancellationTokenSource();
    _ = ConnectWithRetriesAsync(_hubConnection, _connectCts.Token);
  }

  // The toast text is unchanged (the integration tests match on it); the originating checkout's
  // trace id rides along as a hover title and a console line, so a toast can be traced back to
  // the order behind it.
  void ShowToast(NotificationMessage message)
  {
    _logger.LogInformation("Toast {MessageType} received (trace {TraceId})", message.MessageType, message.TraceId);
    _snackbar.Add(builder =>
    {
      builder.OpenElement(0, "span");
      if (message.TraceId is not null)
        builder.AddAttribute(1, "title", $"trace {message.TraceId}");
      builder.AddContent(2, message.Text);
      builder.CloseElement();
    }, Severity.Success, key: message.Text);
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
      // Out of attempts, or disposed mid-connect - give up quietly; the app works fine
      // without order toasts.
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
