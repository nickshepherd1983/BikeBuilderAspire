namespace BikeBuilder.Web.Public.Components.Store;

public sealed record GuestDetails(string Name, string? Email);

// Collects the guest purchaser's details on the first add-to-cart; the storefront has no
// user accounts, so this name is all an order is tied to.
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
