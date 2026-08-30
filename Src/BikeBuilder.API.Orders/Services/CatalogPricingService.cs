using System.Globalization;
using Grpc.Core;

namespace BikeBuilder.API.Orders.Services;

// Snapshots a product's name and price from the catalog bounded context at
// add-to-order time, over the API's anonymous gRPC-Web read endpoints.
public class CatalogPricingService(
    ComponentService.ComponentServiceClient _components,
    BikeBuildService.BikeBuildServiceClient _bikeBuilds)
{
  public async Task<(string Name, decimal UnitPrice)> GetProductAsync(
      ProductType productType, int productId, CancellationToken cancellationToken)
  {
    try
    {
      // The API serializes costs/totals as invariant-culture decimal strings on the wire.
      if (productType == ProductType.Component)
      {
        var component = await _components.GetComponentAsync(
            new GetComponentRequest { Id = productId }, cancellationToken: cancellationToken);
        return (component.Name, decimal.Parse(component.Cost, NumberStyles.Number, CultureInfo.InvariantCulture));
      }

      var bikeBuild = await _bikeBuilds.GetBikeBuildAsync(
          new GetBikeBuildRequest { Id = productId }, cancellationToken: cancellationToken);
      return (bikeBuild.Name, decimal.Parse(bikeBuild.Total, NumberStyles.Number, CultureInfo.InvariantCulture));
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
    {
      throw new GraphQLException(ErrorBuilder.New()
          .SetMessage($"{productType} {productId} was not found in the catalog.")
          .SetCode("PRODUCT_NOT_FOUND")
          .Build());
    }
  }
}
