namespace BikeBuilder.Web.Public.Services;

public sealed record CatalogProduct(int Id, string Name, decimal Price, string Description, bool HasImage);

// Storefront view of the catalog bounded context, over the API's anonymous gRPC-Web reads.
public class CatalogClient(
    ComponentService.ComponentServiceClient _components,
    BikeBuildService.BikeBuildServiceClient _bikeBuilds)
{
  public async Task<(IReadOnlyList<CatalogProduct> Products, int TotalCount)> ListComponentsAsync(
      string? search, int page, int pageSize, CancellationToken cancellationToken = default)
  {
    var response = await _components.ListComponentsAsync(
        new ListComponentsRequest { Search = search ?? "", Page = page, Limit = pageSize },
        cancellationToken: cancellationToken);

    return ([.. response.Components.Select(c => new CatalogProduct(c.Id, c.Name, ParsePrice(c.Cost), c.Description, c.HasImage))],
        response.TotalCount);
  }

  public async Task<(IReadOnlyList<CatalogProduct> Products, int TotalCount)> ListBikeBuildsAsync(
      string? search, int page, int pageSize, CancellationToken cancellationToken = default)
  {
    var response = await _bikeBuilds.ListBikeBuildsAsync(
        new ListBikeBuildsRequest { Search = search ?? "", Page = page, PageSize = pageSize },
        cancellationToken: cancellationToken);

    return ([.. response.BikeBuilds.Select(b => new CatalogProduct(b.Id, b.Name, ParsePrice(b.Total), b.Description, false))],
        response.TotalCount);
  }

  // The API serializes costs/totals as invariant-culture decimal strings on the wire.
  static decimal ParsePrice(string value) =>
      decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
}
