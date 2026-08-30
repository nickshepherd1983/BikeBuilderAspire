using Microsoft.JSInterop;

namespace BikeBuilder.Web.Public.Services;

// Remembers the visitor's draft order across visits via plain localStorage. Plain rather
// than ProtectedLocalStorage on purpose: under InteractiveAuto the same visitor renders on
// the server circuit one visit and in WebAssembly the next, and a plain invariant int is
// the one format both runtimes can read (the order id isn't a secret - every order
// operation the storefront uses is anonymous anyway). Browser storage is only reachable
// once rendering is interactive, so callers load lazily after first render.
public class OrderState(IJSRuntime _js)
{
  const string StorageKey = "bikebuilder-order-id";

  public async Task<int?> GetOrderIdAsync()
  {
    try
    {
      var stored = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
      if (stored is null)
        return null;

      if (int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderId))
        return orderId;

      // A leftover ProtectedLocalStorage payload from the Blazor Server era - clear it so
      // the junk doesn't linger; the visitor just starts a fresh cart.
      await ClearAsync();
      return null;
    }
    catch (InvalidOperationException)
    {
      // Prerendering or a disconnected circuit - treat as "no order yet".
      return null;
    }
  }

  public async Task SetOrderIdAsync(int orderId) =>
      await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, orderId.ToString(CultureInfo.InvariantCulture));

  public async Task ClearAsync() => await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
}
