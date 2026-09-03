namespace BikeBuilder.Test.Integration;

[Collection("BikeBuilderApp")]
public class OrderSmokeTests(BikeBuilderAppFixture fixture)
{
  [Fact]
  public async Task Can_buy_a_component_as_guest_and_receive_a_confirmation_email_and_notifications()
  {
    var storePage = await fixture.CreatePageAsync();
    var wasmPage = await fixture.CreatePageAsync();
    var notificationPage = await fixture.CreatePageAsync();
    var consoleMessages = PageDiagnostics.Attach(storePage);

    try
    {
      await RunScenarioAsync(storePage, wasmPage, notificationPage);
    }
    catch
    {
      await CaptureFailureAsync(storePage, consoleMessages);
      throw;
    }
    finally
    {
      await BikeBuilderAppFixture.SaveVideoAsync(storePage, "order-smoke-store");
      await BikeBuilderAppFixture.SaveVideoAsync(wasmPage, "order-smoke-app");
      await BikeBuilderAppFixture.SaveVideoAsync(notificationPage, "order-smoke-toasts");
    }
  }

  [Fact]
  public async Task Declined_card_keeps_the_cart()
  {
    var storePage = await fixture.CreatePageAsync();
    var consoleMessages = PageDiagnostics.Attach(storePage);

    try
    {
      await RunDeclinedScenarioAsync(storePage);
    }
    catch
    {
      await CaptureFailureAsync(storePage, consoleMessages);
      throw;
    }
    finally
    {
      await BikeBuilderAppFixture.SaveVideoAsync(storePage, "order-declined-store");
    }
  }

  async Task RunScenarioAsync(IPage storePageRaw, IPage wasmPage, IPage notificationPage)
  {
    const string buyerName = "Playwright Buyer";
    const string shipToCity = "Springfield";
    // Unique per run: the smtp4dev inbox is matched on recipient, and outside the session-
    // scoped test containers a developer's persistent catcher keeps earlier runs' receipts.
    var buyerEmail = $"buyer-{Guid.NewGuid():N}@example.com";
    var store = new StorePage(storePageRaw, fixture.WebPublicBaseAddress);
    var checkout = new CheckoutPage(storePageRaw);
    var notifications = new NotificationFeedPage(notificationPage, fixture.WebPublicBaseAddress);

    // Log into the WASM app first (the first navigation drives the stub OIDC login):
    // MainLayout's OrderNotificationsConnection connects to Web.Public's hub once the user
    // is authorized, and it must be live before the order below is processed. The settle
    // delay follows the same convention as NotificationFeedPage.GotoAsync.
    await NavigationHelper.GotoAndWaitForHeadingAsync(wasmPage, $"{fixture.WebBaseAddress}/components", "Components");
    await Task.Delay(TimeSpan.FromSeconds(5));

    // Web.Public's own toast page, connected before anything is ordered.
    await notifications.GotoAsync();

    // Guest shopping: the catalog is seeded with 1000+ components and 100 builds, so
    // "first visible product" is deterministic without depending on specific seeded names.
    await store.GotoAsync();
    var componentName = await store.GetFirstProductNameAsync();
    await store.AddFirstProductToCartAsync(guestName: buyerName, guestEmail: buyerEmail);
    await Expect(store.CartItem(componentName)).ToBeVisibleAsync(new() { Timeout = 30_000 });

    await store.SwitchToTabAsync("Bike Builds");
    var buildName = await store.GetFirstProductNameAsync();
    await store.AddFirstProductToCartAsync();
    await Expect(store.CartItem(buildName)).ToBeVisibleAsync(new() { Timeout = 30_000 });
    await Expect(store.CartTotal).ToBeVisibleAsync(new() { Timeout = 30_000 });

    // Change of heart: drop the bike build again, leaving a single-item order.
    await store.RemoveItemAsync(buildName);
    await Expect(store.CartItem(buildName)).ToBeHiddenAsync(new() { Timeout = 30_000 });
    // ...and prove the RIGHT item survived the removal.
    await Expect(store.CartItem(componentName)).ToBeVisibleAsync(new() { Timeout = 30_000 });

    // The unsubmitted cart lives in Redis, not SQL, and the back office can see it there
    // while the shopper is still deciding.
    var inProcessOrders = new InProcessOrdersPage(wasmPage, fixture.WebBaseAddress);
    await inProcessOrders.GotoAsync();
    await Expect(inProcessOrders.Row(buyerName)).ToBeVisibleAsync(new() { Timeout = 30_000 });

    // Checkout: the contact block is prefilled from the draft (name + email), so only the
    // address, shipping choice and card need typing. Express is the one option whose price
    // differs from the default, which is what proves the choice reached the order.
    await store.GoToCheckoutAsync();
    await checkout.WaitForLoadedAsync();
    await Expect(storePageRaw.GetByLabel("Your name")).ToHaveValueAsync(buyerName);
    await checkout.FillShippingAddressAsync(buyerName, "742 Evergreen Terrace", shipToCity, "IL", "62704", "United States");
    await checkout.ChooseShippingAsync("Express");
    await Expect(checkout.SummaryLine("Shipping")).ToHaveTextAsync("Shipping: $24.99");
    await checkout.FillCardAsync(CheckoutPage.ApprovedCard, buyerName, "12/30", "123");
    await checkout.PlaceOrderAsync();
    await checkout.WaitForOrderConfirmationAsync(buyerName);

    // The confirmation panel echoes back what was captured - and only the card summary.
    await Expect(checkout.Confirmation).ToContainTextAsync(shipToCity);
    await Expect(checkout.Confirmation).ToContainTextAsync("Express");
    await Expect(checkout.Confirmation).ToContainTextAsync("Visa •••• 4242");
    await Expect(checkout.Confirmation).Not.ToContainTextAsync("4242 4242");
    // ...and promises the receipt this test goes on to collect.
    await Expect(checkout.Confirmation).ToContainTextAsync($"a receipt is on its way to {buyerEmail}");
    var orderId = await checkout.GetOrderIdAsync();

    // The OrderPlaced event fans out over Service Bus -> Web.Public's listener -> its hub:
    // the anonymous public page sees it on the general feed, and the authenticated WASM
    // page on the dedicated order method.
    var expectedToast = $"New order placed by {buyerName}: 1 item(s),";
    await notifications.WaitForNotificationAsync(expectedToast);
    await ToastHelper.WaitForToastAsync(wasmPage, expectedToast);

    // The OrderConfirmationRequested event takes the other queue: Service Bus -> the
    // notifications Functions worker -> SMTP into smtp4dev. The receipt names the item, the
    // shipping choice and its price, the destination, and the card summary's last four - the
    // digits rather than the bullet glyphs keep the assertion charset-agnostic.
    using var mail = new Smtp4devClient(fixture.Smtp4devBaseAddress);
    var receipt = await mail.WaitForMessageAsync(buyerEmail,
        m => m.Subject == $"Your BikeBuilder order #{orderId}", TimeSpan.FromSeconds(90));
    var receiptText = await mail.GetPlainTextAsync(receipt.Id);
    Assert.Contains(componentName, receiptText);
    Assert.Contains("Shipping (Express): $24.99", receiptText);
    Assert.Contains(shipToCity, receiptText);
    Assert.Contains("4242", receiptText);
    Assert.DoesNotContain("4242 4242", receiptText);

    // Placing the order cleared the stored draft-order id, so the cart is empty again.
    await store.GotoAsync();
    await Expect(store.EmptyCartMessage).ToBeVisibleAsync(new() { Timeout = 30_000 });

    // Processing claimed the draft out of Redis, so it drops off the in-process list. The
    // page polls every 5s, so Expect's own polling is what absorbs that lag.
    await inProcessOrders.GotoAsync();
    await Expect(inProcessOrders.Row(buyerName)).ToBeHiddenAsync(new() { Timeout = 30_000 });

    // Back office: the signed-in web app's Orders page lists the placed order - buyer,
    // status, the single purchased item, and the checkout details.
    var ordersPage = new OrdersPage(wasmPage, fixture.WebBaseAddress);
    await ordersPage.GotoAsync();
    var orderRow = ordersPage.Row(buyerName);
    await Expect(orderRow).ToBeVisibleAsync(new() { Timeout = 30_000 });
    await Expect(orderRow.GetByText("Placed")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    await Expect(orderRow.GetByText(componentName, new() { Exact = false }).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
    await Expect(orderRow).ToContainTextAsync(shipToCity);
    await Expect(orderRow).ToContainTextAsync("Express");
    await Expect(orderRow).ToContainTextAsync("Visa •••• 4242");
  }

  // A rejected checkout must not cost the shopper their cart: the service validates and
  // authorizes the card before it claims the draft out of Redis, so a decline leaves the
  // cart exactly where it was. Its own buyer name keeps it apart from the happy path's rows.
  async Task RunDeclinedScenarioAsync(IPage storePageRaw)
  {
    const string buyerName = "Playwright Declined";
    var store = new StorePage(storePageRaw, fixture.WebPublicBaseAddress);
    var checkout = new CheckoutPage(storePageRaw);

    await store.GotoAsync();
    var componentName = await store.GetFirstProductNameAsync();
    await store.AddFirstProductToCartAsync(guestName: buyerName);
    await Expect(store.CartItem(componentName)).ToBeVisibleAsync(new() { Timeout = 30_000 });

    await store.GoToCheckoutAsync();
    await checkout.WaitForLoadedAsync();
    await checkout.FillShippingAddressAsync(buyerName, "1 Declined Way", "Shelbyville", "IL", "62565", "United States");
    await checkout.FillCardAsync(CheckoutPage.DeclinedCard, buyerName, "12/30", "123");
    await checkout.PlaceOrderAsync();
    await ToastHelper.WaitForToastAsync(storePageRaw, "declined");
    await Expect(checkout.Confirmation).ToBeHiddenAsync();

    // Back on the store page the same cart, with the same item, is still there.
    await store.GotoAsync();
    await Expect(store.CartItem(componentName)).ToBeVisibleAsync(new() { Timeout = 30_000 });
  }

  async Task CaptureFailureAsync(IPage storePage, List<string> consoleMessages)
  {
    var resultsDir = Path.Combine(AppContext.BaseDirectory, "TestResults");
    Directory.CreateDirectory(resultsDir);
    var id = Guid.NewGuid().ToString("N");
    await storePage.ScreenshotAsync(new() { Path = Path.Combine(resultsDir, $"failure-{id}.png"), FullPage = true });
    await PageDiagnostics.WriteAsync(consoleMessages, Path.Combine(resultsDir, $"failure-{id}-console.log"));
    // The storefront is Blazor Server: the interesting traffic (GraphQL mutations,
    // resilience retries) only shows in the apps' own logs, not the browser's.
    await fixture.DumpResourceLogsAsync($"failure-{id}");
  }
}
