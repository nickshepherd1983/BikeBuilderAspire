namespace BikeBuilder.Web.Public.Services;

// Where the visitor's draft-order id string lives between visits. The web hosts keep it in
// browser localStorage; the MAUI app keeps it in platform preferences. OrderState owns the
// parsing/validation either way - implementations just move raw strings.
public interface IOrderIdStorage
{
  Task<string?> GetAsync();
  Task SetAsync(string value);
  Task RemoveAsync();
}
