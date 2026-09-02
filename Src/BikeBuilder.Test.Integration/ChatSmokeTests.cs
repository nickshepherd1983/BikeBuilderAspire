namespace BikeBuilder.Test.Integration;

[Collection("BikeBuilderApp")]
public class ChatSmokeTests(BikeBuilderAppFixture fixture)
{
  // CI has no Ollama, so there the page's "not reachable" banner is the expected state and
  // the test proves only that the chat host and MCP server boot in the topology, the
  // Admin-gated page renders, and its status call round-trips through the gateway with the
  // user's token. On a machine where Ollama is running it goes on to ask a question and waits
  // for a real answer, exercising the whole model -> MCP -> services loop.
  [Fact]
  public async Task Admin_can_open_the_assistant_page()
  {
    var page = await fixture.CreatePageAsync();
    var consoleMessages = PageDiagnostics.Attach(page);

    try
    {
      await RunScenarioAsync(page);
    }
    catch
    {
      var resultsDir = Path.Combine(AppContext.BaseDirectory, "TestResults");
      Directory.CreateDirectory(resultsDir);
      var id = Guid.NewGuid().ToString("N");
      await page.ScreenshotAsync(new() { Path = Path.Combine(resultsDir, $"failure-{id}-chat.png"), FullPage = true });
      await PageDiagnostics.WriteAsync(consoleMessages, Path.Combine(resultsDir, $"failure-{id}-console.log"));
      await fixture.DumpResourceLogsAsync($"failure-{id}");
      throw;
    }
    finally
    {
      await BikeBuilderAppFixture.SaveVideoAsync(page, "chat-smoke");
    }
  }

  async Task RunScenarioAsync(IPage page)
  {
    await NavigationHelper.GotoAndWaitForHeadingAsync(page, $"{fixture.WebBaseAddress}/chat", "Assistant");

    // The composer and the suggestion chips are the page's interactive surface; both render
    // whether or not a model is installed.
    await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Send" })).ToBeVisibleAsync(new() { Timeout = 30_000 });
    await Expect(page.GetByText("Which bike build has the best average rating?")).ToBeVisibleAsync();

    // The Admin-gated nav link is there for the Admin test user.
    await Expect(page.Locator("nav").GetByRole(AriaRole.Link, new() { Name = "Assistant" })).ToBeVisibleAsync();

    // The status call reached the chat host: with no model on the machine the page says so,
    // and with one it stays quiet - either way there is no Blazor error banner.
    await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

    // A developer machine with Ollama running gets the full loop exercised too: ask the first
    // suggestion and wait for an answer bubble that isn't the "could not reach" failure. Wide
    // timeout - a cold local model loads before it answers, and the tool calls add round trips.
    var missingModel = page.GetByText("Ollama is not reachable").Or(page.GetByText("is not installed"));
    if (await missingModel.CountAsync() > 0)
      return;

    await page.GetByText("Which bike build has the best average rating?").ClickAsync();
    var answer = page.GetByRole(AriaRole.Log).Locator(".chat-bubble-assistant");
    await Expect(answer).ToBeVisibleAsync(new() { Timeout = 240_000 });
    await Expect(answer).Not.ToContainTextAsync("Could not reach");
  }
}
