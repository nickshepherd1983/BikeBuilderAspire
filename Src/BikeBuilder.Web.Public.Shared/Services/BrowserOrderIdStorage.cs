using Microsoft.JSInterop;

namespace BikeBuilder.Web.Public.Services;

// Plain localStorage rather than ProtectedLocalStorage on purpose: under InteractiveAuto the
// same visitor renders on the server circuit one visit and in WebAssembly the next, and a
// plain string is the one format both runtimes can read (the order id isn't a secret - every
// order operation the storefront uses is anonymous anyway).
public class BrowserOrderIdStorage(IJSRuntime _js) : IOrderIdStorage
{
  const string StorageKey = "bikebuilder-order-id";

  public async Task<string?> GetAsync() =>
      await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);

  public async Task SetAsync(string value) =>
      await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, value);

  public async Task RemoveAsync() =>
      await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
}
