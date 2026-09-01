namespace BikeBuilder.Web.Pages;

public partial class Admin(AdminClient _adminClient, IDialogService _dialogService, ISnackbar _snackbar)
{
  static readonly DialogOptions _dialogOptions = new() { MaxWidth = MaxWidth.Small, FullWidth = true };

  UserDirectoryCapabilities? _capabilities;
  List<DirectoryUser> _users = [];
  bool _loading = true;

  protected override async Task OnInitializedAsync()
  {
    try
    {
      _capabilities = await _adminClient.GetCapabilitiesAsync();
      _users = await _adminClient.ListUsersAsync();
    }
    catch (Exception ex) when (ex is HttpRequestException or AccessTokenNotAvailableException)
    {
      _snackbar.Add("Could not reach the admin API.", Severity.Error);
      _capabilities ??= new UserDirectoryCapabilities("none", false, false);
    }
    finally
    {
      _loading = false;
    }
  }

  async Task CreateUser()
  {
    var dialog = await _dialogService.ShowAsync<UserDialog>("New User", _dialogOptions);
    var result = await dialog.Result;
    if (result is null || result.Canceled || result.Data is not CreateDirectoryUserRequest request)
      return;

    var response = await _adminClient.CreateUserAsync(request);
    if (!response.IsSuccessStatusCode)
    {
      _snackbar.Add($"Creating the user failed ({(int)response.StatusCode}).", Severity.Error);
      return;
    }

    _snackbar.Add($"User \"{request.Username}\" created.", Severity.Success);
    _users = await _adminClient.ListUsersAsync();
  }

  async Task EditRoles(DirectoryUser user)
  {
    var parameters = new DialogParameters<UserRolesDialog>
    {
      { x => x.Username, user.Username },
      { x => x.SelectedRoles, user.Roles.ToArray() }
    };

    var dialog = await _dialogService.ShowAsync<UserRolesDialog>("Edit Roles", parameters, _dialogOptions);
    var result = await dialog.Result;
    if (result is null || result.Canceled || result.Data is not string[] roles)
      return;

    var response = await _adminClient.SetRolesAsync(user.Id, roles);
    if (!response.IsSuccessStatusCode)
    {
      _snackbar.Add($"Updating roles failed ({(int)response.StatusCode}).", Severity.Error);
      return;
    }

    _snackbar.Add($"Roles updated for \"{user.Username}\".", Severity.Success);
    _users = await _adminClient.ListUsersAsync();
  }
}
