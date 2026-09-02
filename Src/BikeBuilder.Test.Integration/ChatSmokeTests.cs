namespace BikeBuilder.Test.Integration;

[Collection("BikeBuilderApp")]
public class ChatSmokeTests(BikeBuilderAppFixture fixture)
{
  // CI has no Ollama, so there the window's "not reachable" banner is the expected state and
  // the test proves only that the chat host and MCP server boot in the topology, the
  // role-gated window renders and stays open across pages, and its status call round-trips
  // through the gateway with the user's token. On a machine where Ollama is running it goes
  // on to ask a question and waits for a real answer, exercising the whole model -> MCP ->
  // services loop.
  [Fact]
  public async Task Admin_can_open_the_assistant_window_and_keep_it_across_pages()
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
    // Any page will do - the window belongs to the layout. Home is anonymous, so the login
    // detour happens on the first protected navigation below instead.
    await NavigationHelper.GotoAndWaitForHeadingAsync(page, $"{fixture.WebBaseAddress}/components", "Components");

    var openButton = page.GetByRole(AriaRole.Button, new() { Name = "Open assistant" });
    await Expect(openButton).ToBeVisibleAsync(new() { Timeout = 30_000 });
    await openButton.ClickAsync();

    var sendButton = page.GetByRole(AriaRole.Button, new() { Name = "Send" });
    await Expect(sendButton).ToBeVisibleAsync();
    await Expect(page.GetByText("Which bike build has the best average rating?")).ToBeVisibleAsync();

    // Navigating to another page keeps the window open: the layout, not the page, owns it.
    // The footer's Home link is a plain in-app anchor (Blazor intercepts it client-side); the
    // mini drawer's links hide their text until hovered, which makes them awkward to click.
    await page.Locator(".app-footer").GetByRole(AriaRole.Link, new() { Name = "Home" }).ClickAsync();
    // Level 3: the app bar's title is an h6 with the same text, and a strict role query must
    // match exactly one element.
    await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Bike Builder Admin", Exact = true, Level = 3 })).ToBeVisibleAsync(new() { Timeout = 30_000 });
    await Expect(sendButton).ToBeVisibleAsync();
    await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

    // A developer machine with Ollama running gets the full loop exercised too: ask the first
    // suggestion and wait for an answer bubble that isn't the "could not reach" failure. Wide
    // timeout - a cold local model loads before it answers, and the tool calls add round trips.
    var missingModel = page.GetByText("Ollama is not reachable")
        .Or(page.GetByText("is not installed"))
        .Or(page.GetByText("could not be reached"));
    if (await missingModel.CountAsync() > 0)
      return;

    // The components question yields tabular data, which exercises the Markdown table path.
    await page.GetByText("Which are the five most expensive components?").ClickAsync();
    var answer = page.GetByRole(AriaRole.Log).Locator(".chat-bubble-assistant");
    await Expect(answer).ToBeVisibleAsync(new() { Timeout = 240_000 });
    await Expect(answer).Not.ToContainTextAsync("Could not reach");

    // Keep a picture of the rendered answer (Markdown tables, lists) next to the videos - the
    // one artifact that shows what the model actually produced on this machine.
    var resultsDir = Path.Combine(AppContext.BaseDirectory, "TestResults");
    Directory.CreateDirectory(resultsDir);
    await page.Locator(".assistant-panel").ScreenshotAsync(new() { Path = Path.Combine(resultsDir, "chat-answer.png") });
  }
}
