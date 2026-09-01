using System.Collections.Concurrent;
using System.Net.Http.Json;

namespace BikeBuilder.API.UserAdmin;

// User administration against the integration tests' oidc-server-mock container. The pinned
// image (0.8.6) supports creating users at runtime (POST /api/v1/user) but not updating or
// deleting them, so role edits are Auth0-mode only. The mock has no list endpoint either -
// this keeps an in-memory registry seeded with the user the AppHost configures at startup.
sealed class OidcMockUserDirectory : IUserDirectory
{
  // The mock's config binder is strict about property names and expects the PascalCase used
  // in its env-var configuration - the web-default camelCase gets a 500 back.
  static readonly JsonSerializerOptions PascalCaseJson = new();

  readonly HttpClient _http;
  readonly ConcurrentDictionary<string, DirectoryUser> _users = new(StringComparer.Ordinal);

  public OidcMockUserDirectory(HttpClient http, IConfiguration config)
  {
    _http = http;
    _http.BaseAddress = new Uri(config["UserAdmin:MockUrl"]
        ?? throw new InvalidOperationException("UserAdmin:MockUrl is not configured."));
    // Mirrors USERS_CONFIGURATION_INLINE in the AppHost's test branch.
    _users["test-user"] = new DirectoryUser("test-user", "testuser", "Test User", [Roles.Admin]);
  }

  public UserDirectoryCapabilities Capabilities { get; } = new("mock", CanCreateUsers: true, CanEditRoles: false);

  public Task<IReadOnlyList<DirectoryUser>> ListUsersAsync(CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<DirectoryUser>>(
          _users.Values.OrderBy(u => u.Username, StringComparer.Ordinal).ToList());

  public async Task<DirectoryUser> CreateUserAsync(CreateDirectoryUserRequest request, CancellationToken ct)
  {
    var name = string.IsNullOrWhiteSpace(request.Name) ? request.Username : request.Name;
    var claims = new List<object> { new { Type = "name", Value = name, ValueType = "string" } };
    claims.AddRange(request.Roles.Select(role => (object)new { Type = "role", Value = role, ValueType = "string" }));

    using var response = await _http.PostAsJsonAsync("api/v1/user", new
    {
      SubjectId = request.Username,
      request.Username,
      request.Password,
      Claims = claims,
    }, PascalCaseJson, ct);
    response.EnsureSuccessStatusCode();

    var user = new DirectoryUser(request.Username, request.Username, name, request.Roles);
    _users[user.Id] = user;
    return user;
  }

  public Task SetRolesAsync(string userId, string[] roles, CancellationToken ct) =>
      throw new NotSupportedException("The test OIDC server cannot change existing users' roles.");
}

// Placeholder when neither Auth0 Management credentials nor a mock URL are configured, so
// the Admin page renders a "not configured" notice instead of the API failing to start.
sealed class NullUserDirectory : IUserDirectory
{
  public UserDirectoryCapabilities Capabilities { get; } = new("none", CanCreateUsers: false, CanEditRoles: false);

  public Task<IReadOnlyList<DirectoryUser>> ListUsersAsync(CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<DirectoryUser>>([]);

  public Task<DirectoryUser> CreateUserAsync(CreateDirectoryUserRequest request, CancellationToken ct) =>
      throw new NotSupportedException("User administration is not configured.");

  public Task SetRolesAsync(string userId, string[] roles, CancellationToken ct) =>
      throw new NotSupportedException("User administration is not configured.");
}
