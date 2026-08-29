using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace BikeBuilder.Web.Layout;

public partial class OrderNotificationsConnection(ISnackbar _snackbar, IConfiguration _configuration) : IAsyncDisposable
{
  HubConnection? _hubConnection;

  protected override async Task OnInitializedAsync()
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

    await _hubConnection.StartAsync();
  }

  public async ValueTask DisposeAsync()
  {
    if (_hubConnection is not null)
      await _hubConnection.DisposeAsync();
  }
}
