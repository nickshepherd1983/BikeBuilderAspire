namespace BikeBuilder.Web.Public.Components.Store;

public sealed record GuestDetails(string Name, string? Email);

// Collects the guest purchaser's name on the first add-to-cart so the back office's In Process
// view can label the cart; the storefront has no user accounts. The checkout page asks again
// (prefilled) alongside the address and payment, and its answer is what the order stores.
public partial class GuestDetailsDialog
{
  [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;

  MudForm _form = null!;
  string _name = string.Empty;
  string _email = string.Empty;

  async Task Submit()
  {
    await _form.ValidateAsync();
    if (!_form.IsValid)
      return;

    MudDialog.Close(DialogResult.Ok(new GuestDetails(_name, string.IsNullOrWhiteSpace(_email) ? null : _email)));
  }

  void Cancel() => MudDialog.Cancel();
}
