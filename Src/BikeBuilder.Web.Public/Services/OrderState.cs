using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace BikeBuilder.Web.Public.Services;

// Remembers the visitor's draft order across circuits/refreshes via browser storage.
// Browser storage is only reachable once the circuit is interactive, so callers load lazily
// after first render (the same constraint Home.razor.cs works around for its hub connection).
public class OrderState(ProtectedLocalStorage _storage)
{
  const string StorageKey = "bikebuilder-order-id";

  public async Task<int?> GetOrderIdAsync()
  {
    try
    {
      var stored = await _storage.GetAsync<int>(StorageKey);
      return stored.Success ? stored.Value : null;
    }
    catch (InvalidOperationException)
    {
      // Prerendering or a disconnected circuit - treat as "no order yet".
      return null;
    }
  }

  public async Task SetOrderIdAsync(int orderId) => await _storage.SetAsync(StorageKey, orderId);

  public async Task ClearAsync() => await _storage.DeleteAsync(StorageKey);
}
