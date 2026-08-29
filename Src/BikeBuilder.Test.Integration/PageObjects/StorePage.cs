namespace BikeBuilder.Test.Integration.PageObjects;

// Drives the Web.Public storefront at /store: browse the catalog tabs, build up a guest
// cart, and process the order.
public class StorePage(IPage page, string baseUrl)
{
  public async Task GotoAsync()
  {
    await page.GotoAsync($"{baseUrl}/store");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Store", Exact = true }).WaitForAsync();

    // The heading prerenders before the interactive circuit is up (and the circuit is what
    // loads the catalog and cart) - same settle convention as NotificationHomePage.
    await Task.Delay(TimeSpan.FromSeconds(2));
  }

  // Product cards carry an Add-to-cart button; the cart card doesn't, so this filter
  // excludes it.
  ILocator ProductCards => page.Locator(".mud-card").Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Add to cart" }) });

  public ILocator CartItem(string productName) => page.Locator(".mud-list-item").Filter(new() { HasText = productName });

  public ILocator CartTotal => page.GetByText(new System.Text.RegularExpressions.Regex(@"^Total: \$"));

  public ILocator EmptyCartMessage => page.GetByText("Your cart is empty.");

  public async Task<string> GetFirstProductNameAsync() =>
      (await ProductCards.First.Locator("h6").InnerTextAsync()).Trim();

  /// <summary>
  /// Adds the first visible product to the cart. Pass <paramref name="guestName"/> on the
  /// first add only - that's when the storefront pops the guest-details dialog.
  /// </summary>
  public async Task AddFirstProductToCartAsync(string? guestName = null, string? guestEmail = null)
  {
    await ProductCards.First.GetByRole(AriaRole.Button, new() { Name = "Add to cart" }).ClickAsync();

    if (guestName is not null)
    {
      await page.GetByLabel("Your name").FillAsync(guestName);
      if (guestEmail is not null)
        await page.GetByLabel("Email (optional)").FillAsync(guestEmail);
      await page.GetByRole(AriaRole.Button, new() { Name = "Start order" }).ClickAsync();
    }
  }

  public async Task SwitchToTabAsync(string tabName)
  {
    await page.GetByRole(AriaRole.Tab, new() { Name = tabName }).ClickAsync();
    // Wait until the other tab's catalog page has rendered.
    await ProductCards.First.WaitForAsync();
  }

  public Task RemoveItemAsync(string productName) =>
      page.GetByRole(AriaRole.Button, new() { Name = $"Remove {productName}" }).ClickAsync();

  public Task ProcessOrderAsync() =>
      page.GetByRole(AriaRole.Button, new() { Name = "Process order" }).ClickAsync();

  public Task WaitForOrderConfirmationAsync(string buyerName, float timeout = 10_000) =>
      ToastHelper.WaitForToastAsync(page, $"Order placed — thanks, {buyerName}!", timeout);
}
