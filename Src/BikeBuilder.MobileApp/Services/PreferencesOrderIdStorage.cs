namespace BikeBuilder.MobileApp;

// The MAUI counterpart of the web's localStorage: the draft-order id survives app restarts
// in platform preferences. Same key as the browser for symmetry; the value never leaves the
// device (the id isn't a secret - every storefront order operation is anonymous).
public class PreferencesOrderIdStorage : IOrderIdStorage
{
  const string StorageKey = "bikebuilder-order-id";

  public Task<string?> GetAsync() =>
      Task.FromResult(Preferences.Default.Get<string?>(StorageKey, null));

  public Task SetAsync(string value)
  {
    Preferences.Default.Set(StorageKey, value);
    return Task.CompletedTask;
  }

  public Task RemoveAsync()
  {
    Preferences.Default.Remove(StorageKey);
    return Task.CompletedTask;
  }
}
