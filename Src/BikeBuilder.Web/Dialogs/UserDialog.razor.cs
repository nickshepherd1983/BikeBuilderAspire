namespace BikeBuilder.Web.Dialogs;

public partial class UserDialog
{
  [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;

  MudForm _form = null!;
  string _username = string.Empty;
  string _password = string.Empty;
  string _email = string.Empty;
  string _name = string.Empty;
  readonly HashSet<string> _selectedRoles = [];

  void ToggleRole(string role, bool selected)
  {
    if (selected)
      _selectedRoles.Add(role);
    else
      _selectedRoles.Remove(role);
  }

  async Task Submit()
  {
    await _form.ValidateAsync();
    if (!_form.IsValid)
      return;

    MudDialog.Close(DialogResult.Ok(new CreateDirectoryUserRequest(
        _username,
        _password,
        string.IsNullOrWhiteSpace(_email) ? null : _email,
        string.IsNullOrWhiteSpace(_name) ? null : _name,
        _selectedRoles.ToArray())));
  }

  void Cancel() => MudDialog.Cancel();
}
