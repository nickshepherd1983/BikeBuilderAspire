namespace BikeBuilder.Web.Admin.Pages;

// Sealed so the plain Dispose below is the whole story - there's no subclass to protect
// against, and pages are never inherited from.
public sealed partial class InProcessOrders(OrdersClient _ordersClient, ISnackbar _snackbar) : IDisposable
{
  // Drafts appear and expire without anything telling this page about it, so it polls. Short
  // enough that a cart started in another browser shows up while you're still looking at the
  // page; long enough that an idle tab isn't hammering the orders service.
  static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

  readonly CancellationTokenSource _refreshCts = new();

  bool _loading = true;
  List<DraftOrderDto> _drafts = [];

  protected override async Task OnInitializedAsync()
  {
    // The first load reports failures; the background refreshes below stay quiet.
    try
    {
      _drafts = await _ordersClient.ListDraftsAsync(_refreshCts.Token);
    }
    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
    {
      _snackbar.Add(ErrorReference.Format($"Could not load in process orders: {ex.Message}", ClientTracing.CurrentTraceId), Severity.Error);
    }
    finally
    {
      _loading = false;
    }

    _ = RefreshLoopAsync();
  }

  async Task RefreshLoopAsync()
  {
    using var timer = new PeriodicTimer(RefreshInterval);
    try
    {
      while (await timer.WaitForNextTickAsync(_refreshCts.Token))
      {
        _drafts = await _ordersClient.ListDraftsAsync(_refreshCts.Token);
        // Re-renders the expiry countdowns too - they're computed from ExpiresAt at render
        // time, so every tick moves them along without any per-row timer.
        await InvokeAsync(StateHasChanged);
      }
    }
    catch (OperationCanceledException)
    {
      // Navigated away.
    }
    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
    {
      // A blip on a background refresh isn't worth a toast (and would repeat every tick) -
      // the page just keeps showing the last good list and stops polling.
    }
  }

  static TimeSpan TimeRemaining(DraftOrderDto draft)
  {
    var remaining = draft.ExpiresAt - DateTimeOffset.UtcNow;
    return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
  }

  // A cart that's already lapsed can still be listed for one poll interval, so "expired" is
  // a real state to render rather than a negative countdown.
  static string FormatTimeRemaining(DraftOrderDto draft)
  {
    var remaining = TimeRemaining(draft);
    return remaining == TimeSpan.Zero ? "expired" : $"{remaining:hh\\:mm\\:ss}";
  }

  static Color ExpiryColour(DraftOrderDto draft) => TimeRemaining(draft) switch
  {
    { TotalSeconds: 0 } => Color.Error,
    { TotalMinutes: < 10 } => Color.Warning,
    _ => Color.Default
  };

  public void Dispose()
  {
    _refreshCts.Cancel();
    _refreshCts.Dispose();
  }
}
