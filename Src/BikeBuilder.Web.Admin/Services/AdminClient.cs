namespace BikeBuilder.Web.Admin.Services;

// Talks to BikeBuilder.API's /api/admin endpoints (Admin-only, through the gateway root, so
// rooted paths are correct here - same as ComponentImageClient).
public class AdminClient(HttpClient http)
{
  public Task<UserDirectoryCapabilities?> GetCapabilitiesAsync(CancellationToken ct = default) =>
      http.GetFromJsonAsync<UserDirectoryCapabilities>("/api/admin/capabilities", ct);

  public async Task<List<DirectoryUser>> ListUsersAsync(CancellationToken ct = default) =>
      await http.GetFromJsonAsync<List<DirectoryUser>>("/api/admin/users", ct) ?? [];

  public Task<HttpResponseMessage> CreateUserAsync(CreateDirectoryUserRequest request, CancellationToken ct = default) =>
      http.PostAsJsonAsync("/api/admin/users", request, ct);

  public Task<HttpResponseMessage> SetRolesAsync(string userId, string[] roles, CancellationToken ct = default) =>
      http.PutAsJsonAsync($"/api/admin/users/{Uri.EscapeDataString(userId)}/roles", roles, ct);
}

public sealed record DirectoryUser(string Id, string Username, string? Name, List<string> Roles);

public sealed record CreateDirectoryUserRequest(string Username, string Password, string? Email, string? Name, string[] Roles);

public sealed record UserDirectoryCapabilities(string Mode, bool CanCreateUsers, bool CanEditRoles);
