namespace BikeBuilder.Contracts.Authorization;

// Role and policy names shared by the WASM app (client-side gating), the services
// (enforcement), and the AppHost (test OIDC user seeding). Contracts carries no ASP.NET
// references, so policies are expressed as data - each app folds Policies.All into its own
// AddAuthorization/AddAuthorizationCore registration.

public static class Roles
{
  public const string ComponentEditor = "ComponentEditor";
  public const string BikeBuilder = "BikeBuilder";
  public const string OrderViewer = "OrderViewer";
  // The assistant chat window in the admin app. Its order tools still need OrderViewer (or
  // Admin) - the role grants the conversation, not the data behind every tool.
  public const string Assistant = "Assistant";
  public const string Admin = "Admin";

  public static readonly IReadOnlyList<string> All =
  [
    ComponentEditor,
    BikeBuilder,
    OrderViewer,
    Assistant,
    Admin,
  ];
}

public static class Policies
{
  public const string ManageComponents = "ManageComponents";
  public const string ManageBikeBuilds = "ManageBikeBuilds";
  public const string ViewOrders = "ViewOrders";
  public const string UseAssistant = "UseAssistant";
  public const string AdminOnly = "AdminOnly";

  public static readonly IReadOnlyList<(string Name, string[] AllowedRoles)> All =
  [
    (ManageComponents, [Roles.ComponentEditor, Roles.Admin]),
    (ManageBikeBuilds, [Roles.BikeBuilder, Roles.Admin]),
    (ViewOrders, [Roles.OrderViewer, Roles.Admin]),
    (UseAssistant, [Roles.Assistant, Roles.Admin]),
    (AdminOnly, [Roles.Admin]),
  ];
}

public static class RoleClaim
{
  public const string ConfigKey = "Auth0:RoleClaim";

  // Auth0 drops non-namespaced custom claims, so the default is the namespaced type the
  // post-login Action mints (see the README runbook); the test stub overrides to "role".
  public const string Default = "https://bikebuilder/roles";

  public static string Resolve(string? configured) =>
      string.IsNullOrEmpty(configured) ? Default : configured;
}
