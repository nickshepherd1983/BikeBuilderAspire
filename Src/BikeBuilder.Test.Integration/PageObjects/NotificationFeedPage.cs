namespace BikeBuilder.Test.Integration.PageObjects;

// A Web.Public tab parked on the homepage (the storefront) purely to watch the live activity
// toasts: new components, new bike builds, ratings and orders. The connection itself lives in
// MainLayout, so any route would do - the homepage is just the cheapest one to sit on.
public class NotificationFeedPage(IPage page, string baseUrl)
{
  public async Task GotoAsync()
  {
    await page.GotoAsync(baseUrl);
    await page.GetByRole(AriaRole.Heading, new() { Name = "Store", Exact = true }).WaitForAsync();

    // That heading is present in the static-prerendered HTML, well before Blazor Server's
    // interactive circuit (and the real SignalR HubConnection MainLayout opens once
    // interactive) has finished connecting - settle briefly so callers can rely on the
    // notification hub connection actually being live once this method returns.
    await Task.Delay(TimeSpan.FromSeconds(5));
  }

  public Task WaitForNotificationAsync(string expectedTextSubstring, float timeout = 30_000) =>
      Expect(page.GetByRole(AriaRole.Alert).Filter(new() { HasText = expectedTextSubstring }))
          .ToBeVisibleAsync(new() { Timeout = timeout });
}
