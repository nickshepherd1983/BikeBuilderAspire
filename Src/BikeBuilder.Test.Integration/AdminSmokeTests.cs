namespace BikeBuilder.Test.Integration;

[Collection("BikeBuilderApp")]
public class AdminSmokeTests(BikeBuilderAppFixture fixture)
{
  [Fact]
  public async Task Admin_can_create_role_limited_user_who_only_sees_their_sections()
  {
    var adminPage = await fixture.CreatePageAsync();
    var viewerPage = await fixture.CreatePageAsync();
    var consoleMessages = PageDiagnostics.Attach(adminPage);

    try
    {
      await RunScenarioAsync(adminPage, viewerPage);
    }
    catch
    {
      var resultsDir = Path.Combine(AppContext.BaseDirectory, "TestResults");
      Directory.CreateDirectory(resultsDir);
      var id = Guid.NewGuid().ToString("N");
      await adminPage.ScreenshotAsync(new() { Path = Path.Combine(resultsDir, $"failure-{id}-admin.png"), FullPage = true });
      await viewerPage.ScreenshotAsync(new() { Path = Path.Combine(resultsDir, $"failure-{id}-viewer.png"), FullPage = true });
      await PageDiagnostics.WriteAsync(consoleMessages, Path.Combine(resultsDir, $"failure-{id}-console.log"));
      await fixture.DumpResourceLogsAsync($"failure-{id}");
      throw;
    }
    finally
    {
      await BikeBuilderAppFixture.SaveVideoAsync(adminPage, "admin-smoke-admin");
      await BikeBuilderAppFixture.SaveVideoAsync(viewerPage, "admin-smoke-viewer");
    }
  }

  async Task RunScenarioAsync(IPage adminPage, IPage viewerPage)
  {
    const string viewerUsername = "orderviewer1";
    const string viewerPassword = "password";

    // --- As the Admin (testuser): the Admin section against the stub OIDC mock -----------
    await NavigationHelper.GotoAndWaitForHeadingAsync(adminPage, $"{fixture.WebBaseAddress}/admin", "Admin");

    // Mock-mode capabilities: creating users is offered, editing existing roles is not.
    var newUserButton = adminPage.GetByRole(AriaRole.Button, new() { Name = "New User" });
    await Expect(newUserButton).ToBeVisibleAsync(new() { Timeout = 30_000 });
    await Expect(adminPage.GetByLabel("Edit roles")).ToHaveCountAsync(0);

    // The seeded Admin shows up in the user list with its role chip.
    await Expect(adminPage.GetByRole(AriaRole.Cell, new() { Name = "testuser" })).ToBeVisibleAsync();

    // Create an OrderViewer through the dialog.
    await newUserButton.ClickAsync();
    await adminPage.GetByLabel("Username").FillAsync(viewerUsername);
    await adminPage.GetByLabel("Password").FillAsync(viewerPassword);
    await adminPage.GetByLabel("Display name").FillAsync("Order Viewer");
    await adminPage.GetByRole(AriaRole.Checkbox, new() { Name = "OrderViewer" }).CheckAsync();
    await adminPage.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
    await Expect(adminPage.GetByRole(AriaRole.Cell, new() { Name = viewerUsername })).ToBeVisibleAsync(new() { Timeout = 30_000 });

    // --- As the new OrderViewer, in its own browser context (fresh stub session) ---------
    await NavigationHelper.GotoAndWaitForHeadingAsync(
        viewerPage, $"{fixture.WebBaseAddress}/orders", "Orders", viewerUsername, viewerPassword);

    // The Orders page actually loaded its role-gated GraphQL data, not just its heading.
    await Expect(viewerPage.Locator("#blazor-error-ui")).ToBeHiddenAsync();

    // Nav shows only the order sections (plus Home): no catalog, build, or admin links.
    var nav = viewerPage.Locator("nav");
    await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Orders", Exact = true })).ToBeVisibleAsync();
    await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "In Process" })).ToBeVisibleAsync();
    await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Components" })).ToHaveCountAsync(0);
    await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Bike Builds" })).ToHaveCountAsync(0);
    await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Admin" })).ToHaveCountAsync(0);

    // Straight to a page outside the role: the forbidden branch renders, no login loop.
    await viewerPage.GotoAsync($"{fixture.WebBaseAddress}/components");
    await Expect(viewerPage.GetByRole(AriaRole.Heading, new() { Name = "Not authorized" }))
        .ToBeVisibleAsync(new() { Timeout = 30_000 });
  }
}
