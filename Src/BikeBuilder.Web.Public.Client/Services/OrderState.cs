using Microsoft.JSInterop;

namespace BikeBuilder.Web.Public.Services;

// Remembers the visitor's draft order across visits via plain localStorage. Plain rather
// than ProtectedLocalStorage on purpose: under InteractiveAuto the same visitor renders on
// the server circuit one visit and in WebAssembly the next, and a plain string is the one
// format both runtimes can read (the order id isn't a secret - every order operation the
// storefront uses is anonymous anyway). Browser storage is only reachable once rendering is
// interactive, so callers load lazily after first render.
public class OrderState(IJSRuntime _js)
{
  const string StorageKey = "bikebuilder-order-id";

  public async Task<Guid?> GetOrderIdAsync()
  {
    try
    {
      var stored = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
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

  public async Task SetOrderIdAsync(Guid orderId) =>
      await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, orderId.ToString());

  public async Task ClearAsync() => await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
}
