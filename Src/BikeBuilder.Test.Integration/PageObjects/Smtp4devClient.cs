using System.Net.Http.Json;
using System.Text.Json;

namespace BikeBuilder.Test.Integration.PageObjects;

// Reads the smtp4dev inbox over its REST API (the same one its web UI uses). Not a page
// object in the Playwright sense, but it plays the same role: the test's view of a system
// boundary. Polls with its own deadline because a receipt arrives asynchronously (Service Bus
// -> Functions worker -> SMTP) and RetryHelper is attempt-based rather than time-based.
public sealed class Smtp4devClient(string baseAddress) : IDisposable
{
  static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  readonly HttpClient _http = new() { BaseAddress = new Uri(baseAddress) };

  public async Task<MessageSummary> WaitForMessageAsync(string toAddress, Func<MessageSummary, bool> match, TimeSpan timeout)
  {
    var deadline = DateTimeOffset.UtcNow + timeout;
    var seen = new List<string>();
    while (true)
    {
      // searchTerms narrows the server-side scan; the recipient/subject filters below are
      // what the assertion actually rests on.
      var page = await _http.GetFromJsonAsync<PagedResult>(
          $"/api/Messages?searchTerms={Uri.EscapeDataString(toAddress)}&mailboxName=Default&sortColumn=receivedDate&sortIsDescending=true&page=1&pageSize=50",
          Json);
      var messages = page?.Results ?? [];
      var hit = messages.FirstOrDefault(m =>
          m.To.Any(to => to.Contains(toAddress, StringComparison.OrdinalIgnoreCase)) && match(m));
      if (hit is not null)
        return hit;

      seen = messages.Select(m => $"{m.Subject} -> {string.Join(",", m.To)}").ToList();
      if (DateTimeOffset.UtcNow >= deadline)
        break;
      await Task.Delay(TimeSpan.FromSeconds(2));
    }

    throw new TimeoutException(
        $"No email for {toAddress} matched within {timeout}. Inbox: [{string.Join("; ", seen)}]");
  }

  public Task<string> GetPlainTextAsync(Guid id) => _http.GetStringAsync($"/api/Messages/{id}/plaintext");

  public void Dispose() => _http.Dispose();

  public sealed record PagedResult(List<MessageSummary> Results);

  public sealed record MessageSummary(Guid Id, string From, string[] To, string Subject, DateTime ReceivedDate);
}
