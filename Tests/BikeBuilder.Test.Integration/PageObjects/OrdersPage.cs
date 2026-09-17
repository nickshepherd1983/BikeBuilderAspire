namespace BikeBuilder.Test.Integration.PageObjects;

// The signed-in web app's back-office Orders page.
public class OrdersPage(IPage page, string baseUrl)
{
  public Task GotoAsync() =>
      NavigationHelper.GotoAndWaitForHeadingAsync(page, $"{baseUrl}/orders", "Orders");

  public ILocator Row(string textSubstring) =>
      page.Locator("table tbody tr").Filter(new() { HasText = textSubstring });
}
