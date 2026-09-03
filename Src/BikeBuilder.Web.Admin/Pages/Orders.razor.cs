namespace BikeBuilder.Web.Admin.Pages;

public partial class Orders(OrdersClient _ordersClient, ISnackbar _snackbar)
{
  bool _loading = true;
  List<OrderDto> _orders = [];

  protected override async Task OnInitializedAsync()
  {
    try
    {
      _orders = await _ordersClient.ListAsync();
    }
    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
    {
      _snackbar.Add(ErrorReference.Format($"Could not load orders: {ex.Message}", ClientTracing.CurrentTraceId), Severity.Error);
    }
    finally
    {
      _loading = false;
    }
  }

  // GraphQL serializes enums as SCREAMING_SNAKE ("PLACED", "EXPRESS") - display them as words.
  static string FormatStatus(string status) =>
      status.Length == 0 ? status : char.ToUpperInvariant(status[0]) + status[1..].ToLowerInvariant();
}
