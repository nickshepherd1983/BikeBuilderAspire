using Microsoft.AspNetCore.SignalR.Client;

namespace BikeBuilder.Web.Public.Components.Pages;

public partial class Home(ISnackbar _snackbar, INotificationsHubUrlProvider _hubUrl) : IAsyncDisposable
{
  HubConnection? _hubConnection;

  protected override async Task OnInitializedAsync()
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

    _hubConnection.On<string>("ReceiveNotification",
        message => InvokeAsync(() => _snackbar.Add(message, Severity.Info)));

    await _hubConnection.StartAsync();
  }

  public async ValueTask DisposeAsync()
  {
    if (_hubConnection is not null)
      await _hubConnection.DisposeAsync();
  }
}
