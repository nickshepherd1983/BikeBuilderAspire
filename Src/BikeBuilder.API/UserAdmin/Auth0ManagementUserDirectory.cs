using System.Net.Http.Json;

namespace BikeBuilder.API.UserAdmin;

// User administration against a real Auth0 tenant via the Management API. Requires an M2M
// application authorized for the Management API (see the README runbook) and these keys -
// the secret belongs in user secrets, never appsettings:
//   Auth0:Management:Domain        e.g. dev-xyz.us.auth0.com
//   Auth0:Management:ClientId
//   Auth0:Management:ClientSecret
sealed class Auth0ManagementUserDirectory(HttpClient http, Auth0ManagementTokenProvider tokenProvider) : IUserDirectory
{
  public UserDirectoryCapabilities Capabilities { get; } = new("auth0", CanCreateUsers: true, CanEditRoles: true);

  public async Task<IReadOnlyList<DirectoryUser>> ListUsersAsync(CancellationToken ct)
  {
    var roleIds = await GetRoleIdsAsync(ct);

    // Base list first, then one members call per known role - a fixed 1 + N_roles requests
    // rather than a users×roles fan-out.
    var users = await GetAsync<List<Auth0User>>("users?per_page=50&sort=created_at:1", ct);
    var rolesByUserId = new Dictionary<string, List<string>>();
    foreach (var (roleName, roleId) in roleIds)
    {
      var members = await GetAsync<List<Auth0User>>($"roles/{Uri.EscapeDataString(roleId)}/users", ct);
      foreach (var memberId in members.Select(member => member.user_id))
      {
        if (!rolesByUserId.TryGetValue(memberId, out var list))
        {
          list = [];
          rolesByUserId[memberId] = list;
        }
        list.Add(roleName);
      }
    }

    return users
        .Select(u => new DirectoryUser(
            u.user_id,
            u.email ?? u.name ?? u.user_id,
            u.name,
            rolesByUserId.TryGetValue(u.user_id, out var roles) ? roles : []))
        .ToList();
  }

  public async Task<DirectoryUser> CreateUserAsync(CreateDirectoryUserRequest request, CancellationToken ct)
  {
    // Auth0 database connections key users by email; treat a plain username as the email
    // when no separate one is supplied.
    var email = string.IsNullOrWhiteSpace(request.Email) ? request.Username : request.Email;
    var created = await SendAsync<Auth0User>(HttpMethod.Post, "users", new
    {
      connection = "Username-Password-Authentication",
      email,
      password = request.Password,
      name = string.IsNullOrWhiteSpace(request.Name) ? email : request.Name,
    }, ct);

    if (request.Roles.Length > 0)
      await AssignRolesAsync(created.user_id, request.Roles, ct);

    return new DirectoryUser(created.user_id, email, created.name, request.Roles);
  }

  public async Task SetRolesAsync(string userId, string[] roles, CancellationToken ct)
  {
    var current = await GetAsync<List<Auth0Role>>($"users/{Uri.EscapeDataString(userId)}/roles", ct);
    var currentNames = current.Select(r => r.name).ToHashSet(StringComparer.Ordinal);
    var wanted = roles.ToHashSet(StringComparer.Ordinal);

    var toAdd = wanted.Except(currentNames).ToArray();
    var toRemove = currentNames.Except(wanted).ToArray();

    if (toAdd.Length > 0)
      await AssignRolesAsync(userId, toAdd, ct);
    if (toRemove.Length > 0)
    {
      var roleIds = await GetRoleIdsAsync(ct);
      var ids = toRemove.Where(roleIds.ContainsKey).Select(r => roleIds[r]).ToArray();
      if (ids.Length > 0)
        await SendAsync<object?>(HttpMethod.Delete, $"users/{Uri.EscapeDataString(userId)}/roles", new { roles = ids }, ct);
    }
  }

  async Task AssignRolesAsync(string userId, string[] roles, CancellationToken ct)
  {
    var roleIds = await GetRoleIdsAsync(ct);
    var ids = roles.Where(roleIds.ContainsKey).Select(r => roleIds[r]).ToArray();
    if (ids.Length > 0)
      await SendAsync<object?>(HttpMethod.Post, $"users/{Uri.EscapeDataString(userId)}/roles", new { roles = ids }, ct);
  }

  async Task<Dictionary<string, string>> GetRoleIdsAsync(CancellationToken ct)
  {
    // Only the application's own role names matter; anything else in the tenant is ignored.
    var all = await GetAsync<List<Auth0Role>>("roles", ct);
    return all
        .Where(r => Roles.All.Contains(r.name))
        .ToDictionary(r => r.name, r => r.id, StringComparer.Ordinal);
  }

  async Task<T> GetAsync<T>(string path, CancellationToken ct)
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    return await SendCoreAsync<T>(request, ct);
  }

  async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken ct)
  {
    using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
    return await SendCoreAsync<T>(request, ct);
  }

  async Task<T> SendCoreAsync<T>(HttpRequestMessage request, CancellationToken ct)
  {
    request.Headers.Authorization = new("Bearer", await tokenProvider.GetTokenAsync(ct));
    using var response = await http.SendAsync(request, ct);
    response.EnsureSuccessStatusCode();
    if (typeof(T) == typeof(object) || response.Content.Headers.ContentLength is 0 or null)
      return default!;
    return (await response.Content.ReadFromJsonAsync<T>(ct))!;
  }

#pragma warning disable IDE1006, S101 // Property names mirror the Auth0 wire format.
  sealed record Auth0User(string user_id, string? email, string? name);
  sealed record Auth0Role(string id, string name);
#pragma warning restore IDE1006, S101
}

// Client-credentials token for the Management API, cached until shortly before expiry.
sealed class Auth0ManagementTokenProvider(HttpClient http, IConfiguration config)
{
  readonly SemaphoreSlim _gate = new(1, 1);
  string? _token;
  DateTimeOffset _expiresAt;

  public async Task<string> GetTokenAsync(CancellationToken ct)
  {
    if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
      return _token;

    await _gate.WaitAsync(ct);
    try
    {
      if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
        return _token;

      var domain = config["Auth0:Management:Domain"]
          ?? throw new InvalidOperationException("Auth0:Management:Domain is not configured.");
      using var response = await http.PostAsJsonAsync($"https://{domain}/oauth/token", new
      {
        grant_type = "client_credentials",
        client_id = config["Auth0:Management:ClientId"],
        client_secret = config["Auth0:Management:ClientSecret"],
        audience = $"https://{domain}/api/v2/",
      }, ct);
      response.EnsureSuccessStatusCode();

      var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
      _token = payload.GetProperty("access_token").GetString()!;
      _expiresAt = DateTimeOffset.UtcNow.AddSeconds(payload.GetProperty("expires_in").GetInt32() - 60);
      return _token;
    }
    finally
    {
      _gate.Release();
    }
  }
}
