namespace BikeBuilder.Test.Integration;

[Collection("BikeBuilderApp")]
public class OrderSmokeTests(BikeBuilderAppFixture fixture)
{
  [Fact]
  public async Task Can_buy_a_component_as_guest_and_see_order_notifications_everywhere()
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
      var resultsDir = Path.Combine(AppContext.BaseDirectory, "TestResults");
      Directory.CreateDirectory(resultsDir);
      var id = Guid.NewGuid().ToString("N");
      await storePage.ScreenshotAsync(new() { Path = Path.Combine(resultsDir, $"failure-{id}.png"), FullPage = true });
      await PageDiagnostics.WriteAsync(consoleMessages, Path.Combine(resultsDir, $"failure-{id}-console.log"));
      // The storefront is Blazor Server: the interesting traffic (GraphQL mutations,
      // resilience retries) only shows in the apps' own logs, not the browser's.
      await fixture.DumpResourceLogsAsync($"failure-{id}");
      throw;
    }
    finally
    {
      await BikeBuilderAppFixture.SaveVideoAsync(storePage, "order-smoke-store");
      await BikeBuilderAppFixture.SaveVideoAsync(wasmPage, "order-smoke-app");
      await BikeBuilderAppFixture.SaveVideoAsync(notificationPage, "order-smoke-toasts");
    }
  }

  async Task RunScenarioAsync(IPage storePageRaw, IPage wasmPage, IPage notificationPage)
  {
    const string buyerName = "Playwright Buyer";
    var store = new StorePage(storePageRaw, fixture.WebPublicBaseAddress);
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
    await store.AddFirstProductToCartAsync(guestName: buyerName, guestEmail: "buyer@example.com");
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

    await store.ProcessOrderAsync();
    await store.WaitForOrderConfirmationAsync(buyerName);

    // The OrderPlaced event fans out over Service Bus -> Web.Public's listener -> its hub:
    // the anonymous public page sees it on the general feed, and the authenticated WASM
    // page on the dedicated order method.
    var expectedToast = $"New order placed by {buyerName}: 1 item(s),";
    await notifications.WaitForNotificationAsync(expectedToast);
    await ToastHelper.WaitForToastAsync(wasmPage, expectedToast);

    // Processing cleared the stored draft-order id, so the cart is empty again.
    await Expect(store.EmptyCartMessage).ToBeVisibleAsync(new() { Timeout = 30_000 });

    // Processing claimed the draft out of Redis, so it drops off the in-process list. The
    // page polls every 5s, so Expect's own polling is what absorbs that lag.
    await inProcessOrders.GotoAsync();
    await Expect(inProcessOrders.Row(buyerName)).ToBeHiddenAsync(new() { Timeout = 30_000 });

    // Back office: the signed-in web app's Orders page lists the placed order - buyer,
    // status, and the single purchased item.
    var ordersPage = new OrdersPage(wasmPage, fixture.WebBaseAddress);
    await ordersPage.GotoAsync();
    var orderRow = ordersPage.Row(buyerName);
    await Expect(orderRow).ToBeVisibleAsync(new() { Timeout = 30_000 });
    await Expect(orderRow.GetByText("Placed")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    await Expect(orderRow.GetByText(componentName, new() { Exact = false }).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
  }
}
