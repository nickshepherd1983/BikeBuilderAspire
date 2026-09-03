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

    // Drag the top-right handle up and to the right: the panel should grow both ways. The
    // upward drag is small because the panel caps its height at the viewport minus 140px, and
    // the recording browser is only 720px tall - the default 560px leaves 20px of headroom.
    var panel = page.Locator(".assistant-panel");
    var before = await panel.BoundingBoxAsync() ?? throw new InvalidOperationException("The assistant panel has no bounding box.");
    var handle = await page.GetByRole(AriaRole.Separator, new() { Name = "Resize assistant" }).BoundingBoxAsync()
        ?? throw new InvalidOperationException("The resize handle has no bounding box.");
    await page.Mouse.MoveAsync(handle.X + handle.Width / 2, handle.Y + handle.Height / 2);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(handle.X + handle.Width / 2 + 120, handle.Y + handle.Height / 2 - 15, new() { Steps = 8 });
    await page.Mouse.UpAsync();
    var after = await panel.BoundingBoxAsync() ?? throw new InvalidOperationException("The assistant panel has no bounding box after resizing.");
    Assert.True(after.Width > before.Width + 100, $"Width went from {before.Width} to {after.Width}.");
    Assert.True(after.Height > before.Height + 10, $"Height went from {before.Height} to {after.Height}.");

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

    // The transcript tweens down to the newest message; give the animation a moment, then
    // confirm it ended at the bottom (within a couple of pixels of rounding).
    await page.WaitForTimeoutAsync(1_500);
    var atBottom = await page.Locator(".assistant-transcript")
        .EvaluateAsync<bool>("el => el.scrollHeight - el.clientHeight - el.scrollTop < 4");
    Assert.True(atBottom, "The transcript did not scroll to the newest message.");

    // Keep a picture of the rendered answer (Markdown tables, lists) next to the videos - the
    // one artifact that shows what the model actually produced on this machine.
    var resultsDir = Path.Combine(AppContext.BaseDirectory, "TestResults");
    Directory.CreateDirectory(resultsDir);
    await page.Locator(".assistant-panel").ScreenshotAsync(new() { Path = Path.Combine(resultsDir, "chat-answer.png") });

    // A follow-up that only makes sense with the first answer in context: the whole
    // transcript (table answer included) is resent, trimmed by the service, and must be
    // accepted rather than bounced for its length.
    await page.GetByLabel("Ask a question").FillAsync("Which manufacturer made the first one in that list?");
    await page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
    await Expect(answer).ToHaveCountAsync(2, new() { Timeout = 240_000 });
    await Expect(answer.Nth(1)).Not.ToContainTextAsync("at most");
    await Expect(answer.Nth(1)).Not.ToContainTextAsync("Could not reach");

    // Close hides the window (the robot button comes back); reopening shows the same chat.
    await page.GetByRole(AriaRole.Button, new() { Name = "Close assistant" }).ClickAsync();
    await Expect(sendButton).ToBeHiddenAsync();
    await openButton.ClickAsync();
    await Expect(answer).ToHaveCountAsync(2);
  }
}
