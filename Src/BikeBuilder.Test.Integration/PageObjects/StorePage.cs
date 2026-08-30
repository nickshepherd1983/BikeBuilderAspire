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
    await Task.Delay(TimeSpan.FromSeconds(5));
  }

  // Product cards carry an Add-to-cart button; the cart card doesn't, so this filter
  // excludes it.
  ILocator ProductCards => page.Locator(".mud-card").Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Add to cart" }) });

  // Every cart locator is scoped INSIDE the cart card: product names also appear in the
  // catalog cards and toast messages, and a page-wide text match can pass against those.
  ILocator CartCard => page.Locator(".mud-card").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Cart", Exact = true }) });

  public ILocator CartItem(string productName) => CartCard.Locator(".mud-list-item").Filter(new() { HasText = productName });

  public ILocator CartTotal => CartCard.GetByText(new System.Text.RegularExpressions.Regex(@"^Total: \$"));

  public ILocator EmptyCartMessage => CartCard.GetByText("Your cart is empty.");

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
    // The old tab's cards are still in the DOM until the new catalog page renders, so a bare
    // "first product card exists" wait passes instantly and lets the next step read (or
    // click) the OLD tab's first product. On a loaded machine that stale read is the norm,
    // not the exception: GetFirstProductNameAsync then returns a product that's already in
    // the cart, and RemoveItemAsync later deletes that cart row instead of the new one.
    // Components and builds never share names, so "first product name changed" is the
    // reliable signal that the other tab's page has actually rendered.
    var staleFirstProduct = await GetFirstProductNameAsync();
    await page.GetByRole(AriaRole.Tab, new() { Name = tabName }).ClickAsync();
    await Expect(ProductCards.First.Locator("h6")).Not.ToHaveTextAsync(staleFirstProduct, new() { Timeout = 30_000 });
  }

  // Click the delete button INSIDE the named cart row (it's the row's only button) rather
  // than matching aria-labels globally - immune to label churn while the list re-renders.
  public Task RemoveItemAsync(string productName) =>
      CartItem(productName).GetByRole(AriaRole.Button).ClickAsync();

  public Task ProcessOrderAsync() =>
      page.GetByRole(AriaRole.Button, new() { Name = "Process order" }).ClickAsync();

  public Task WaitForOrderConfirmationAsync(string buyerName, float timeout = 30_000) =>
      ToastHelper.WaitForToastAsync(page, $"Order placed — thanks, {buyerName}!", timeout);
}
