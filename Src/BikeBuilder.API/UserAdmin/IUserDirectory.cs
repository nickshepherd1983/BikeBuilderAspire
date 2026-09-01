namespace BikeBuilder.API.UserAdmin;

public sealed record DirectoryUser(string Id, string Username, string? Name, IReadOnlyList<string> Roles);

public sealed record CreateDirectoryUserRequest(string Username, string Password, string? Email, string? Name, string[] Roles);

// Mode: "auth0", "mock", or "none" - the Admin page adapts its UI to what the directory
// can actually do (the test OIDC mock can create users but not change existing ones).
public sealed record UserDirectoryCapabilities(string Mode, bool CanCreateUsers, bool CanEditRoles);

// The user store behind the Admin section: Auth0's Management API in real mode, the
// oidc-server-mock container's runtime API in integration tests.
public interface IUserDirectory
{
  UserDirectoryCapabilities Capabilities { get; }

  Task<IReadOnlyList<DirectoryUser>> ListUsersAsync(CancellationToken ct);

  Task<DirectoryUser> CreateUserAsync(CreateDirectoryUserRequest request, CancellationToken ct);

  Task SetRolesAsync(string userId, string[] roles, CancellationToken ct);
}
