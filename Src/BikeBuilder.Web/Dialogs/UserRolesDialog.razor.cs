namespace BikeBuilder.Web.Dialogs;

public partial class UserRolesDialog
{
  [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;

  [Parameter] public string Username { get; set; } = string.Empty;
  [Parameter] public string[] SelectedRoles { get; set; } = [];

  readonly HashSet<string> _selectedRoles = [];

  protected override void OnInitialized() => _selectedRoles.UnionWith(SelectedRoles);

  void ToggleRole(string role, bool selected)
  {
    if (selected)
      _selectedRoles.Add(role);
    else
      _selectedRoles.Remove(role);
  }

  void Submit() => MudDialog.Close(DialogResult.Ok(_selectedRoles.ToArray()));

  void Cancel() => MudDialog.Cancel();
}
