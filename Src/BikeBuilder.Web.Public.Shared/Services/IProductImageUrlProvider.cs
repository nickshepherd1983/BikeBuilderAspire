namespace BikeBuilder.Web.Public.Services;

// Where catalog images load from differs per host: the web storefront proxies them through
// its own origin (browser <img> tags can't attach anything to a cross-origin request), while
// the MAUI app fetches the API's anonymous image endpoint directly.
public interface IProductImageUrlProvider
{
  string GetImageUrl(int componentId);
}
