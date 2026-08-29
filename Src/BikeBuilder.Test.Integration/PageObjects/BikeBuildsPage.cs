using System.Globalization;

namespace BikeBuilder.Test.Integration.PageObjects;

public class BikeBuildsPage(IPage page, string baseUrl)
{
  public Task GotoAsync() =>
      NavigationHelper.GotoAndWaitForHeadingAsync(page, $"{baseUrl}/bikebuilds", "Bike Builds");

  ILocator RowByName(string buildName) =>
      page.Locator("table tbody tr").Filter(new() { HasText = buildName });

  // Columns: Name | Date | Description | Total | Ratings | Average | actions.
  public ILocator RatingsCountCell(string buildName) => RowByName(buildName).Locator("td:nth-child(5)");

  public ILocator AverageRatingCell(string buildName) => RowByName(buildName).Locator("td:nth-child(6)");

  public ILocator Pager => page.Locator(".mud-table-pagination");

  public ILocator Row(string buildName) => RowByName(buildName);

  public ILocator Rows => page.Locator("table tbody tr");

  // Debounced toolbar search - callers must use auto-retrying Expect waits afterwards.
  public Task SearchAsync(string term) => SearchField.FillAsync(term);

  public Task ClearSearchAsync() => SearchField.FillAsync(string.Empty);

  // Clicks a MudTableSortLabel by its visible column text (cycles asc -> desc -> unsorted).
  public Task SortByAsync(string column) =>
      page.Locator(".mud-table-sort-label", new() { HasText = column }).ClickAsync();

  // Columns: Name | Date | Description | Total | Ratings | Average | actions.
  public async Task<IReadOnlyList<decimal>> GetTotalsAsync()
  {
    var texts = await page.Locator("table tbody tr td:nth-child(4)").AllTextContentsAsync();
    // Cells render as currency ("$1,234.56") - strip everything but digits and the decimal point.
    return [.. texts.Select(t => decimal.Parse(new string([.. t.Where(c => char.IsAsciiDigit(c) || c == '.')]), CultureInfo.InvariantCulture))];
  }

  ILocator SearchField => page.GetByPlaceholder("Search name or description");

  public async Task<BikeBuildEditPage> CreateBikeBuildAsync(string name, string description)
  {
    await RetryHelper.RunAsync(async () =>
    {
      var dialog = page.Locator(".mud-dialog");
      if (await dialog.IsVisibleAsync())
      {
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
      }

      // On a starved CI runner the silent OIDC re-auth that follows a full page load can
      // fail and strand the app on a blank authentication redirect; waiting longer never
      // helps, but re-navigating restarts the auth dance cleanly (including the login
      // form, which GotoAsync handles).
      var createButton = page.GetByRole(AriaRole.Button, new() { Name = "Create Bike Build" });
      if (!await createButton.IsVisibleAsync())
        await GotoAsync();

      await createButton.ClickAsync();
      await dialog.GetByLabel("Name").FillAsync(name);
      await dialog.GetByLabel("Description").FillAsync(description);
      await dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

      await page.GetByRole(AriaRole.Heading, new() { Name = "Edit Bike Build" }).WaitForAsync(new() { Timeout = 30_000 });
    }, maxAttempts: 3);

    return new BikeBuildEditPage(page);
  }
}
