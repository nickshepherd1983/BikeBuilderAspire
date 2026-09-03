namespace BikeBuilder.Test.Integration.PageObjects;

// Captures a page's console output, page errors, and failed HTTP traffic into one list that
// tests dump to TestResults on failure. Console text alone proved useless in practice: a
// "Failed to load resource: ... 404" message carries no URL, so log the network events (which
// do) alongside it.
public static class PageDiagnostics
{
  public static List<string> Attach(IPage page)
  {
    var messages = new List<string>();

    page.Console += (_, msg) => Add(messages, $"[{msg.Type}] {msg.Text}");
    page.PageError += (_, error) => Add(messages, $"[pageerror] {error}");
    page.RequestFailed += (_, request) =>
        Add(messages, $"[requestfailed] {request.Method} {request.Url} - {request.Failure}");
    page.Response += (_, response) =>
    {
      if (response.Status >= 400)
      {
        // Every service answers with its W3C trace id (Playwright lowercases header names), so
        // a failed browser request can be looked up in the apps' logs and the dashboard.
        var trace = response.Headers.TryGetValue("x-trace-id", out var traceId) ? $" trace={traceId}" : string.Empty;
        Add(messages, $"[response {response.Status}] {response.Request.Method} {response.Url}{trace}");
      }
    };

    return messages;
  }

  // Handlers fire on Playwright's dispatcher thread while the test thread may be reading
  // the list for its failure dump - serialize access.
  static void Add(List<string> messages, string line)
  {
    lock (messages)
      messages.Add($"{DateTime.Now:HH:mm:ss.fff} {line}");
  }

  public static async Task WriteAsync(List<string> messages, string path)
  {
    string[] snapshot;
    lock (messages)
      snapshot = [.. messages];
    await File.WriteAllLinesAsync(path, snapshot);
  }
}
