using ArchUnitNET.Loader;

namespace BikeBuilder.Test.Architecture;

// The assemblies the rules run over, each named by a type that lives in it so a renamed or
// removed project fails to compile here instead of silently dropping out of the rules.
public static class Assemblies
{
  public static readonly Assembly Api = typeof(global::BikeBuilder.API.Data.BikeBuilderDbContext).Assembly;
  public static readonly Assembly Orders = typeof(global::BikeBuilder.API.Orders.Data.OrdersDbContext).Assembly;
  public static readonly Assembly Ratings = typeof(global::BikeBuilder.API.Ratings.Functions.RatingsFunctions).Assembly;
  public static readonly Assembly Notifications = typeof(global::BikeBuilder.API.Notifications.Functions.NotificationFunctions).Assembly;
  public static readonly Assembly Chat = typeof(global::BikeBuilder.API.Chat.Services.ChatService).Assembly;
  public static readonly Assembly Mcp = typeof(global::BikeBuilder.MCP.Tools.CatalogTools).Assembly;
  public static readonly Assembly Contracts = typeof(global::BikeBuilder.Contracts.Events.OrderPlacedEvent).Assembly;
  public static readonly Assembly ServiceDefaults = typeof(global::Microsoft.Extensions.Hosting.Extensions).Assembly;
  public static readonly Assembly DataSeeder = typeof(global::BikeBuilder.DataSeeder.DatabaseSeeder).Assembly;
  public static readonly Assembly WebAdmin = typeof(global::BikeBuilder.Web.Admin.Services.RatingsClient).Assembly;
  public static readonly Assembly Storefront = typeof(global::BikeBuilder.Web.Public.Services.CatalogClient).Assembly;

  // The six deployable services, each a bounded context of its own.
  public static readonly IReadOnlyDictionary<string, Assembly> Services = new Dictionary<string, Assembly>
  {
    ["API"] = Api,
    ["Orders"] = Orders,
    ["Ratings"] = Ratings,
    ["Notifications"] = Notifications,
    ["Chat"] = Chat,
    ["MCP"] = Mcp
  };

  public static readonly Assembly[] All =
      [Api, Orders, Ratings, Notifications, Chat, Mcp, Contracts, ServiceDefaults, DataSeeder, WebAdmin, Storefront];
}

public static class BikeBuilderArchitecture
{
  // Loaded once for every rule in the run; ArchUnitNET caches rule evaluation per architecture.
  public static readonly ArchUnitNET.Domain.Architecture ArchitectureUnderTest =
      new ArchLoader().LoadAssemblies(Assemblies.All).Build();

  // The types a project genuinely owns. ArchUnitNET keys types by full name, and two kinds of
  // type share a full name across projects: the top-level-statements Program (plus its
  // compiler-generated closures) that every host declares, and the gRPC stubs each consumer
  // compiles from BikeBuilder.API's .proto files into its own assembly under
  // BikeBuilder.API.Protos. Both would be attributed to whichever assembly loaded first, so
  // dependency rules leave them out on both sides.
  public static GivenTypesConjunction OwnTypesOf(Assembly assembly, params Assembly[] moreAssemblies) =>
      Types().That().ResideInAssembly(assembly, moreAssemblies)
          .And().DoNotResideInNamespace("BikeBuilder.API.Protos")
          .And().DoNotHaveFullNameMatching("^Program(/|$)");
}
