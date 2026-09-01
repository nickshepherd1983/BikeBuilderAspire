namespace BikeBuilder.Web.Public.Services;

// The web hosts' image source: BikeBuilder.Web.Public's same-origin proxy endpoint.
public class RelativeProductImageUrlProvider : IProductImageUrlProvider
{
  public string GetImageUrl(int componentId) => $"/store/components/{componentId}/image";
}
