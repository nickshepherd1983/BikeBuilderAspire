using System.Text.Json;
using Polly.Timeout;

namespace BikeBuilder.Web.Admin.Services;

// Talks to BikeBuilder.API.Chat through the gateway's /chat prefix. The transcript lives in
// the page and is sent whole each time - the service keeps no conversation state.
public class ChatClient(HttpClient http)
{
  // Relative paths (no leading slash): the base address carries the gateway's /chat prefix,
  // and a rooted path would replace it rather than append to it.
  public Task<ChatStatusDto?> GetStatusAsync(CancellationToken ct = default) =>
      http.GetFromJsonAsync<ChatStatusDto>("api/chat/status", ct);

  public async Task<ChatReplyDto> AskAsync(List<ChatTurnDto> messages, CancellationToken ct = default)
  {
    HttpResponseMessage response;
    try
    {
      response = await http.PostAsJsonAsync("api/chat", new AskRequestDto(messages), ct);
    }
    catch (HttpRequestException ex)
    {
      // The browser's "Failed to fetch": no usable response at all. Through the gateway that
      // means an error the gateway produced itself (a 502 while the chat host is still
      // starting, or a 504 when its proxy timeout elapsed) - those carry no CORS headers, so
      // the browser hides them. Say so rather than echoing the bare TypeError.
      throw new ChatException(
          "The assistant did not answer: the request failed before a response arrived. " +
          "The chat service may still be starting, or the gateway gave up waiting for the model. " +
          "Check the chat and gateway resources in the Aspire dashboard, then try again.", ex);
    }
    catch (TimeoutRejectedException ex)
    {
      throw new ChatException("The assistant took longer than five minutes to answer, so the request was abandoned. Try a narrower question.", ex);
    }

    if (!response.IsSuccessStatusCode)
      throw new ChatException(await DescribeFailureAsync(response, ct));

    return await response.Content.ReadFromJsonAsync<ChatReplyDto>(ct)
        ?? throw new ChatException("The assistant returned an empty reply.");
  }

  // A 503 carries problem details whose detail is written for the user (what to start or
  // pull); anything else falls back to the body or the status code.
  static async Task<string> DescribeFailureAsync(HttpResponseMessage response, CancellationToken ct)
  {
    var body = await response.Content.ReadAsStringAsync(ct);
    try
    {
      var problem = JsonSerializer.Deserialize<ProblemDto>(body, _jsonOptions);
      if (problem?.Detail is { Length: > 0 })
        return problem.Detail;
    }
    catch (JsonException)
    {
      // Not problem details - fall through to the raw body.
    }

    return body.Length > 0 ? body.Trim('"') : $"The assistant request failed ({(int)response.StatusCode}).";
  }

  static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

  sealed record ProblemDto(string? Title, string? Detail);
}

public sealed record ChatTurnDto(string Role, string Content);

public sealed record AskRequestDto(List<ChatTurnDto> Messages);

public sealed record ToolCallDto(string Name, string Arguments, string ResultPreview);

public sealed record ChatReplyDto(string Reply, List<ToolCallDto> ToolCalls, string Model, long ElapsedMs);

public sealed record ChatStatusDto(
    bool OllamaReachable,
    bool ModelAvailable,
    string Model,
    string Endpoint,
    List<string> ToolNames,
    string? McpError);

// A failed assistant request whose message is already worded for the user.
public sealed class ChatException(string message, Exception? innerException = null) : Exception(message, innerException);
