using BikeBuilder.Web.Public.Components.Checkout;

namespace BikeBuilder.Web.Public.Components.Pages;

// Collects everything the order needs beyond its items and hands it to processOrder in one
// go. The cart itself isn't editable here - that stays on the store page, one click away.
public partial class Checkout(
    IOrdersClient _ordersClient,
    OrderState _orderState,
    NavigationManager _navigation,
    ISnackbar _snackbar)
{
  MudForm _form = null!;
  bool _loading = true;
  bool _submitting;
  IDraftOrderParts? _draft;
  IReadOnlyList<IGetShippingOptions_ShippingOptions> _shippingOptions = [];
  // Set once the order is placed; the page then shows the confirmation instead of the form.
  IPlacedOrderParts? _placed;
  readonly CheckoutModel _model = new();

  decimal SelectedShippingCost =>
      _shippingOptions.FirstOrDefault(option => option.Method == _model.ShippingMethod)?.Price ?? 0m;

  protected override async Task OnInitializedAsync()
  {
    // Same prerender guard as Store: the draft id lives in browser storage.
    if (!RendererInfo.IsInteractive)
      return;

    try
    {
      var orderId = await _orderState.GetOrderIdAsync();
      var draftTask = orderId is null ? null : _ordersClient.GetDraftOrder.ExecuteAsync(orderId.Value);
      var optionsTask = _ordersClient.GetShippingOptions.ExecuteAsync();

      _draft = draftTask is null ? null : (await draftTask).Data?.DraftOrder;
      if (_draft is null || _draft.Items.Count == 0)
      {
        // Expired, already processed, or emptied out: nothing to check out. Drop a dead id
        // the same way Store does and send the visitor back to shop.
        if (_draft is null && orderId is not null)
          await _orderState.ClearAsync();
        _snackbar.Add("Your cart is empty — add something first.", Severity.Info);
        _navigation.NavigateTo("/");
        return;
      }

      var options = await optionsTask;
      _shippingOptions = options.Data?.ShippingOptions ?? [];
      if (options.Errors.Count > 0)
        _snackbar.Add(options.Errors[0].Message, Severity.Error);

      // The name typed at add-to-cart is the natural starting point for the contact block.
      _model.Name = _draft.CustomerName;
      _model.Email = _draft.CustomerEmail ?? string.Empty;
    }
    finally
    {
      _loading = false;
    }
  }

  async Task PlaceOrderAsync()
  {
    if (_draft is null)
      return;

    await _form.ValidateAsync();
    if (!_form.IsValid)
    {
      _snackbar.Add("Check the highlighted fields.", Severity.Warning);
      return;
    }

    _submitting = true;
    try
    {
      var result = await _ordersClient.ProcessOrder.ExecuteAsync(_draft.Id, _model.ToInput());
      if (result.Errors.Count > 0)
      {
        // Declined card, expired cart, validation the client missed - the service's message
        // is written for the shopper, so show it as is. The cart is untouched on every one of
        // these (the service validates before it claims the draft).
        _snackbar.Add(result.Errors[0].Message, Severity.Error);
        return;
      }

      _placed = result.Data!.ProcessOrder;
      await _orderState.ClearAsync();
      _snackbar.Add($"Order placed — thanks, {_placed.CustomerName}!", Severity.Success);
    }
    finally
    {
      _submitting = false;
    }
  }

  void UseTestCard()
  {
    _model.CardNumber = "4242 4242 4242 4242";
    _model.CardholderName = string.IsNullOrWhiteSpace(_model.Name) ? "Test Shopper" : _model.Name;
    _model.CardExpiry = "12/30";
    _model.CardCvc = "123";
  }

  string ShippingName(ShippingMethod method) =>
      _shippingOptions.FirstOrDefault(option => option.Method == method)?.Name ?? method.ToString();

  string ShippingEta(ShippingMethod method)
  {
    var option = _shippingOptions.FirstOrDefault(o => o.Method == method);
    return option is null ? string.Empty : Eta(option);
  }

  static string Eta(IGetShippingOptions_ShippingOptions option) =>
      option.MinDays == option.MaxDays
          ? $"{option.MinDays} business day{(option.MinDays == 1 ? "" : "s")}"
          : $"{option.MinDays}–{option.MaxDays} business days";
}
