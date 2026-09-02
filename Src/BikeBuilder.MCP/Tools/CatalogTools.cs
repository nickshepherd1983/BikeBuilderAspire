namespace BikeBuilder.MCP.Tools;

// Read-only catalog tools over the API's anonymous gRPC-Web reads - the same calls the
// storefront makes, so no token is needed and the catalog stays behind its own API.
[McpServerToolType]
public sealed class CatalogTools(
    ComponentService.ComponentServiceClient _components,
    BikeBuildService.BikeBuildServiceClient _bikeBuilds)
{
  [McpServerTool(Name = "search_components", ReadOnly = true, Idempotent = true),
   Description("Searches the component catalog (parts such as forks, tires, stems, wheels). Returns one page of components with id, name, cost, SKU and manufacturer, plus the total number of matches. Sort by cost to find the cheapest or most expensive parts.")]
  public async Task<ComponentPage> SearchComponents(
      [Description("Text matched against component name and SKU. Omit to list everything.")] string? search = null,
      [Description("1-based page number.")] int page = 1,
      [Description("Results per page, 1 to 50.")] int pageSize = ToolSupport.DefaultPageSize,
      [Description("Sort field: name, cost, sku or manufacturer. Defaults to name.")] string? sortBy = null,
      [Description("True to sort from highest or last to lowest or first.")] bool descending = false,
      CancellationToken cancellationToken = default)
  {
    var response = await _components.ListComponentsAsync(new ListComponentsRequest
    {
      Search = search ?? "",
      Page = ToolSupport.Page(page),
      Limit = ToolSupport.PageSize(pageSize),
      SortField = ParseComponentSort(sortBy),
      SortDescending = descending
    }, cancellationToken: cancellationToken);

    return new ComponentPage(
        ToolSupport.Page(page),
        ToolSupport.PageSize(pageSize),
        response.TotalCount,
        [.. response.Components.Select(ToSummary)]);
  }

  [McpServerTool(Name = "get_component", ReadOnly = true, Idempotent = true),
   Description("Gets one component by id, including its full description and typed information (for example fork travel or tire width) when it has any.")]
  public async Task<ComponentDetail> GetComponent(
      [Description("The component id.")] int id,
      CancellationToken cancellationToken = default)
  {
    try
    {
      var component = await _components.GetComponentAsync(new GetComponentRequest { Id = id }, cancellationToken: cancellationToken);
      return new ComponentDetail(
          component.Id,
          component.Name,
          ToolSupport.Money(component.Cost),
          component.Sku,
          component.Manufacturer.ToString(),
          component.Description,
          component.HasImage,
          ToolSupport.ParseJson(component.ComponentInformationJson));
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
    {
      throw new McpException($"Component {id} was not found.");
    }
  }

  [McpServerTool(Name = "search_bike_builds", ReadOnly = true, Idempotent = true),
   Description("Searches the bike builds (complete bikes assembled from components). Returns one page with id, name, date, total price and description, plus the total number of matches. Sort by total to find the most or least expensive builds.")]
  public async Task<BikeBuildPage> SearchBikeBuilds(
      [Description("Text matched against build name and description. Omit to list everything.")] string? search = null,
      [Description("1-based page number.")] int page = 1,
      [Description("Results per page, 1 to 50.")] int pageSize = ToolSupport.DefaultPageSize,
      [Description("Sort field: name, date, description or total. Defaults to newest first.")] string? sortBy = null,
      [Description("True to sort from highest or latest to lowest or earliest.")] bool descending = false,
      CancellationToken cancellationToken = default)
  {
    var response = await _bikeBuilds.ListBikeBuildsAsync(new ListBikeBuildsRequest
    {
      Search = search ?? "",
      Page = ToolSupport.Page(page),
      PageSize = ToolSupport.PageSize(pageSize),
      SortField = ParseBikeBuildSort(sortBy),
      SortDescending = descending
    }, cancellationToken: cancellationToken);

    return new BikeBuildPage(
        ToolSupport.Page(page),
        ToolSupport.PageSize(pageSize),
        response.TotalCount,
        [.. response.BikeBuilds.Select(ToSummary)]);
  }

  [McpServerTool(Name = "get_bike_build", ReadOnly = true, Idempotent = true),
   Description("Gets one bike build by id with its full description, total price and the list of components (with quantities) it is built from.")]
  public async Task<BikeBuildDetail> GetBikeBuild(
      [Description("The bike build id.")] int id,
      CancellationToken cancellationToken = default)
  {
    try
    {
      var build = await _bikeBuilds.GetBikeBuildAsync(new GetBikeBuildRequest { Id = id }, cancellationToken: cancellationToken);
      return new BikeBuildDetail(
          build.Id,
          build.Name,
          ToolSupport.Date(build.Date.ToDateTimeOffset()),
          ToolSupport.Money(build.Total),
          build.Description,
          [.. build.Components.Select(line => new BikeBuildComponentLine(line.ComponentId, line.ComponentName, line.Quantity))]);
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
    {
      throw new McpException($"Bike build {id} was not found.");
    }
  }

  internal static ComponentSummary ToSummary(ComponentMessage component) => new(
      component.Id,
      component.Name,
      ToolSupport.Money(component.Cost),
      component.Sku,
      component.Manufacturer.ToString(),
      ToolSupport.Trim(component.Description),
      component.HasImage);

  internal static BikeBuildSummary ToSummary(BikeBuildMessage build) => new(
      build.Id,
      build.Name,
      ToolSupport.Date(build.Date.ToDateTimeOffset()),
      ToolSupport.Money(build.Total),
      ToolSupport.Trim(build.Description));

  static ComponentSortField ParseComponentSort(string? sortBy) => sortBy?.Trim().ToLowerInvariant() switch
  {
    null or "" => ComponentSortField.Unspecified,
    "name" => ComponentSortField.Name,
    "cost" or "price" => ComponentSortField.Cost,
    "sku" => ComponentSortField.Sku,
    "manufacturer" or "brand" => ComponentSortField.Manufacturer,
    _ => throw new McpException($"Unknown sortBy '{sortBy}'. Use name, cost, sku or manufacturer.")
  };

  static BikeBuildSortField ParseBikeBuildSort(string? sortBy) => sortBy?.Trim().ToLowerInvariant() switch
  {
    null or "" => BikeBuildSortField.Unspecified,
    "name" => BikeBuildSortField.Name,
    "date" => BikeBuildSortField.Date,
    "description" => BikeBuildSortField.Description,
    "total" or "price" or "cost" => BikeBuildSortField.Total,
    _ => throw new McpException($"Unknown sortBy '{sortBy}'. Use name, date, description or total.")
  };
}

// Money and dates are pre-formatted strings ($1,234.56 and MM/dd/yyyy HH:mm UTC) - see ToolSupport.
public sealed record ComponentSummary(int Id, string Name, string Cost, string Sku, string Manufacturer, string Description, bool HasImage);

public sealed record ComponentPage(int Page, int PageSize, int TotalCount, IReadOnlyList<ComponentSummary> Components);

public sealed record ComponentDetail(int Id, string Name, string Cost, string Sku, string Manufacturer, string Description, bool HasImage, JsonElement? Information);

public sealed record BikeBuildSummary(int Id, string Name, string Date, string Total, string Description);

public sealed record BikeBuildPage(int Page, int PageSize, int TotalCount, IReadOnlyList<BikeBuildSummary> BikeBuilds);

public sealed record BikeBuildComponentLine(int ComponentId, string ComponentName, int Quantity);

public sealed record BikeBuildDetail(int Id, string Name, string Date, string Total, string Description, IReadOnlyList<BikeBuildComponentLine> Components);
