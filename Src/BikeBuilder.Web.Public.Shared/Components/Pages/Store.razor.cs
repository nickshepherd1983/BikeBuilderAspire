using BikeBuilder.Web.Public.Components.Store;
using StrawberryShake;

namespace BikeBuilder.Web.Public.Components.Pages;

public partial class Store(
    CatalogClient _catalog,
    IOrdersClient _ordersClient,
    OrderState _orderState,
    IProductImageUrlProvider _imageUrls,
    IDialogService _dialogService,
    NavigationManager _navigation,
    ISnackbar _snackbar)
{
  const int PageSize = 12;

  int _tabIndex;
  string? _search;
  bool _loading = true;
  int _page = 1;
  int _totalCount;
  IReadOnlyList<CatalogProduct> _products = [];
  // The generated fragment interface for the in-progress cart - every draft-returning
  // operation in Orders.graphql implements it. Placing the order happens on the Checkout
  // page, which is why nothing here ever holds a placed Order.
  IDraftOrderParts? _order;

  int PageCount => Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize));

  protected override async Task OnInitializedAsync()
  {
    // Skip prerendering: the catalog calls and browser storage both need the interactive
    // circuit (same guard as Home.razor.cs).
    if (!RendererInfo.IsInteractive)
      return;

    await LoadProductsAsync();

    // Pick up a draft order from a previous visit. Drafts live in Redis under a one-hour
    // sliding TTL, so a null answer means it expired (or was already processed) - either
    // way the stored id is dead and the visitor starts fresh.
    var orderId = await _orderState.GetOrderIdAsync();
    if (orderId is not null)
    {
      var result = await _ordersClient.GetDraftOrder.ExecuteAsync(orderId.Value);
      _order = result.Data?.DraftOrder;
      if (_order is null)
        await _orderState.ClearAsync();
    }
  }

  async Task AddToCartAsync(CatalogProduct product)
  {
    var productType = _tabIndex == 0 ? ProductType.Component : ProductType.BikeBuild;

    if (_order is null)
    {
      // Just a name to label the cart with - addresses and payment wait for the checkout page.
      var dialog = await _dialogService.ShowAsync<GuestDetailsDialog>("Start your order");
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
  async Task RemoveItemAsync(IGetDraftOrder_DraftOrder_Items item)
  {
    if (_order is null)
      return;

    var result = await _ordersClient.RemoveOrderItem.ExecuteAsync(_order.Id, item.Id);
    if (TryGetData(result, out var data))
      _order = data.RemoveOrderItem;
  }

  // The checkout page re-reads the draft from storage, so nothing needs handing over.
  void GoToCheckout() => _navigation.NavigateTo("checkout");

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
      _snackbar.Add(result.Errors[0].ToUserMessage(), Severity.Error);
      data = null!;
      return false;
    }

    data = result.Data!;
    return true;
  }
}
