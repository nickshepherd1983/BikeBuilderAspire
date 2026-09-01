namespace BikeBuilder.Web.Public.Services;

// Remembers the visitor's draft order across visits. The raw string lives wherever the host
// platform keeps it (browser localStorage on the web, preferences on MAUI - see
// IOrderIdStorage); this class owns parsing and cleanup. Browser storage is only reachable
// once rendering is interactive, so callers load lazily after first render.
public class OrderState(IOrderIdStorage _storage)
{
  public async Task<Guid?> GetOrderIdAsync()
  {
    try
    {
      var stored = await _storage.GetAsync();
      if (stored is null)
        return null;

      if (Guid.TryParse(stored, CultureInfo.InvariantCulture, out var orderId))
        return orderId;

      // Junk from an earlier storage format - a ProtectedLocalStorage payload from the
      // Blazor Server era, or the integer id drafts used before they moved to Redis. Clear
      // it so it doesn't linger; the visitor just starts a fresh cart.
      await ClearAsync();
      return null;
    }
    catch (InvalidOperationException)
    {
      // Prerendering or a disconnected circuit - treat as "no order yet".
      return null;
    }
  }

  public async Task SetOrderIdAsync(Guid orderId) => await _storage.SetAsync(orderId.ToString());

  public async Task ClearAsync() => await _storage.RemoveAsync();
}
