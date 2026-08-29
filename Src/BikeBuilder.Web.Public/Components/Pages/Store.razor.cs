using System.Globalization;
using BikeBuilder.Web.Public.Components.Store;
using BikeBuilder.Web.Public.GraphQL;
using BikeBuilder.Web.Public.Services;
using StrawberryShake;

namespace BikeBuilder.Web.Public.Components.Pages;

public partial class Store(
    CatalogClient _catalog,
    IOrdersClient _ordersClient,
    OrderState _orderState,
    IDialogService _dialogService,
    ISnackbar _snackbar)
{
  const int PageSize = 12;

  int _tabIndex;
  string? _search;
  bool _loading = true;
  bool _processing;
  int _page = 1;
  int _totalCount;
  IReadOnlyList<CatalogProduct> _products = [];
  // The generated fragment interface - every mutation payload in Orders.graphql implements it.
  IOrderParts? _order;

  int PageCount => Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize));

  protected override async Task OnInitializedAsync()
  {
    // Skip prerendering: the catalog calls and browser storage both need the interactive
    // circuit (same guard as Home.razor.cs).
    if (!RendererInfo.IsInteractive)
      return;

    await LoadProductsAsync();

    // Pick up a draft order from a previous visit; discard it if it's gone or already placed.
    var orderId = await _orderState.GetOrderIdAsync();
    if (orderId is not null)
    {
      var result = await _ordersClient.GetOrder.ExecuteAsync(orderId.Value);
      var order = result.Data?.Order;
      if (order is not null && order.Status == OrderStatus.Draft)
        _order = order;
      else
        await _orderState.ClearAsync();
    }
  }

  async Task AddToCartAsync(CatalogProduct product)
  {
    var productType = _tabIndex == 0 ? ProductType.Component : ProductType.BikeBuild;

    if (_order is null)
    {
      var dialog = await _dialogService.ShowAsync<GuestDetailsDialog>("Checkout details");
      var dialogResult = await dialog.Result;
      if (dialogResult is null || dialogResult.Canceled || dialogResult.Data is not GuestDetails guest)
        return;

      var created = await _ordersClient.CreateOrder.ExecuteAsync(guest.Name, guest.Email);
      if (!TryGetData(created, out var createdData))
        return;

      _order = createdData.CreateOrder;
      await _orderState.SetOrderIdAsync(createdData.CreateOrder.Id);
    }

    var added = await _ordersClient.AddOrderItem.ExecuteAsync(_order.Id, productType, product.Id, 1);
    if (!TryGetData(added, out var addedData))
      return;

    _order = addedData.AddOrderItem;
    _snackbar.Add($"{product.Name} added to your cart.", Severity.Success);
  }

  // The fragment's item interface is named after the first operation that uses it.
  async Task RemoveItemAsync(IGetOrder_Order_Items item)
  {
    if (_order is null)
      return;

    var result = await _ordersClient.RemoveOrderItem.ExecuteAsync(_order.Id, item.Id);
    if (TryGetData(result, out var data))
      _order = data.RemoveOrderItem;
  }

  async Task ProcessOrderAsync()
  {
    if (_order is null)
      return;

    _processing = true;
    try
    {
      var result = await _ordersClient.ProcessOrder.ExecuteAsync(_order.Id);
      if (!TryGetData(result, out var data))
        return;

      _snackbar.Add($"Order placed — thanks, {data.ProcessOrder.CustomerName}!", Severity.Success);
      _order = null;
      await _orderState.ClearAsync();
    }
    finally
    {
      _processing = false;
    }
  }

  async Task OnSearchAsync(string value)
  {
    _search = value;
    _page = 1;
    await LoadProductsAsync();
  }

  async Task OnTabChangedAsync(int index)
  {
    _tabIndex = index;
    _page = 1;
    await LoadProductsAsync();
  }

  async Task OnPageChangedAsync(int page)
  {
    _page = page;
    await LoadProductsAsync();
  }

  async Task LoadProductsAsync()
  {
    _loading = true;
    try
    {
      (_products, _totalCount) = _tabIndex == 0
          ? await _catalog.ListComponentsAsync(_search, _page, PageSize)
          : await _catalog.ListBikeBuildsAsync(_search, _page, PageSize);
    }
    finally
    {
      _loading = false;
    }
  }

  bool TryGetData<TData>(IOperationResult<TData> result, out TData data) where TData : class
  {
    if (result.Errors.Count > 0)
    {
      _snackbar.Add(result.Errors[0].Message, Severity.Error);
      data = null!;
      return false;
    }

    data = result.Data!;
    return true;
  }

  // Invariant "$" formatting keeps prices identical across machines (the integration test
  // asserts on cart totals).
  static string FormatPrice(decimal value) => $"${value.ToString("N2", CultureInfo.InvariantCulture)}";
}
