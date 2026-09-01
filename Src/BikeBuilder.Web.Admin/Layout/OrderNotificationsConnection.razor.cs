using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace BikeBuilder.Web.Admin.Layout;

public partial class OrderNotificationsConnection(ISnackbar _snackbar, IConfiguration _configuration) : IAsyncDisposable
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

    _hubConnection.On<string>("ReceiveOrderNotification",
        message => _snackbar.Add(message, Severity.Success));

    // Fire-and-forget with retries: order toasts are a nicety, and an unreachable
    // Web.Public must never take the app down (an exception awaited here would). Automatic
    // reconnect only covers drops AFTER a successful start, hence the manual retry loop.
    _connectCts = new CancellationTokenSource();
    _ = ConnectWithRetriesAsync(_hubConnection, _connectCts.Token);
  }

  static async Task ConnectWithRetriesAsync(HubConnection hubConnection, CancellationToken cancellationToken)
  {
    for (var attempt = 1; attempt <= 5 && !cancellationToken.IsCancellationRequested; attempt++)
    {
      try
      {
        await hubConnection.StartAsync(cancellationToken);
        return;
      }
      catch (Exception) when (attempt < 5)
      {
        try
        {
          await Task.Delay(TimeSpan.FromSeconds(3 * attempt), cancellationToken);
        }
        catch (OperationCanceledException)
        {
          return;
        }
      }
      catch (Exception)
      {
        // Out of attempts - give up quietly; the app works fine without order toasts.
        return;
      }
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
