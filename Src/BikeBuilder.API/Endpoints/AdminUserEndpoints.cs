using BikeBuilder.API.UserAdmin;

namespace BikeBuilder.API.Endpoints;

// The Admin section's backend: role administration against whichever IUserDirectory is
// configured (Auth0 Management API, the test OIDC mock, or nothing). Reached by the web app
// through the gateway's catch-all route, Admin-only end to end.
public static class AdminUserEndpoints
{
  public static void MapAdminUserEndpoints(this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/admin").RequireAuthorization(Policies.AdminOnly);

    // Tells the Admin page which operations to offer - the mock can create but not edit.
    group.MapGet("/capabilities", (IUserDirectory directory) => Results.Ok(directory.Capabilities));

    group.MapGet("/users", async (IUserDirectory directory, CancellationToken ct) =>
        Results.Ok(await directory.ListUsersAsync(ct)));

    group.MapPost("/users", async (CreateDirectoryUserRequest request, IUserDirectory directory, CancellationToken ct) =>
    {
      if (!directory.Capabilities.CanCreateUsers)
        return Results.StatusCode(StatusCodes.Status501NotImplemented);
      if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Username and password are required.");
      var unknown = request.Roles.Except(Roles.All, StringComparer.Ordinal).ToArray();
      if (unknown.Length > 0)
        return Results.BadRequest($"Unknown roles: {string.Join(", ", unknown)}");

      return Results.Ok(await directory.CreateUserAsync(request, ct));
    });

    group.MapPut("/users/{id}/roles", async (string id, string[] roles, IUserDirectory directory, CancellationToken ct) =>
    {
      if (!directory.Capabilities.CanEditRoles)
        return Results.StatusCode(StatusCodes.Status501NotImplemented);
      var unknown = roles.Except(Roles.All, StringComparer.Ordinal).ToArray();
      if (unknown.Length > 0)
        return Results.BadRequest($"Unknown roles: {string.Join(", ", unknown)}");

      await directory.SetRolesAsync(id, roles, ct);
      return Results.NoContent();
    });
  }
}
