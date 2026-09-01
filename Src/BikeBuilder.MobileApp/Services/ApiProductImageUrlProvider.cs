namespace BikeBuilder.MobileApp;

// Catalog images come straight from the API's anonymous image endpoint via the gateway.
// The web hosts proxy this through their own origin instead, but that proxy exists only
// for browser same-origin semantics - a WebView loading an absolute URL doesn't need it.
public class ApiProductImageUrlProvider : IProductImageUrlProvider
{
  public string GetImageUrl(int componentId) =>
      $"{AppEnvironment.ApiBaseAddress}/api/components/{componentId}/image";
}
