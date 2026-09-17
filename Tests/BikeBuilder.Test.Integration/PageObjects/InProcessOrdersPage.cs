namespace BikeBuilder.Test.Integration.PageObjects;

// The signed-in web app's view of the carts currently held in Redis.
public class InProcessOrdersPage(IPage page, string baseUrl)
{
  public Task GotoAsync() =>
      NavigationHelper.GotoAndWaitForHeadingAsync(page, $"{baseUrl}/orders/in-process", "In Process Orders");

  public ILocator Row(string textSubstring) =>
      page.Locator("table tbody tr").Filter(new() { HasText = textSubstring });
}
