using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol;
using OllamaSharp;
using OllamaSharp.Models.Exceptions;

namespace BikeBuilder.API.Chat.Services;

// One question in, one answer out: connects to the MCP server as the caller, hands its tools
// to the model, lets the function-invoking pipeline run the lookups, and returns the final
// text with a trace of which tools ran (the page shows it so answers stay checkable).
public sealed class ChatService(
    IChatClient _chatClient,
    OllamaApiClient _ollama,
    OllamaOptions _options,
    McpToolsFactory _mcp,
    ILogger<ChatService> _logger)
{
  // The client resends the whole transcript; older turns are dropped to keep the prompt small
  // for a local model, and tool results are cut to a preview for the trace.
  const int MaxHistoryTurns = 20;
  const int ResultPreviewLength = 500;

  const string SystemPrompt = """
      You are the Bike Builder assistant for the shop's back-office staff. You answer questions about
      the component catalog, bike builds, customer orders and bike build ratings using the tools
      provided.

      Rules:
      - Always look data up with the tools. Never invent ids, names, prices, totals or ratings.
      - Call describe_data first if you are unsure which tool fits the question.
      - Prefer tools that aggregate for you (orders_summary, top_rated_bike_builds, sorting by cost or
        total) over paging through long lists.
      - For what customers say or feel, read the review text: list_ratings for one build,
        search_rating_comments across builds (with a phrase or star range). Quote or summarise the
        comments rather than reporting only star counts.
      - Money is in US dollars: always write it with a dollar sign and two decimals, for example
        $23.99 or $1,234.50 - never a bare number and never another currency symbol.
      - Dates and times are always written as MM/dd/yyyy HH:mm (for example 09/02/2026 14:30);
        tools already return them that way - copy them, do not rephrase or convert them.
      - If a tool reports that orders require a signed-in role, tell the user that plainly.
      - Keep answers concise, and say when one is partial (for example, based on the 100 most
        recent orders).

      Formatting (the answer is rendered as Markdown in a narrow chat panel):
      - Any list of items that carries numbers - prices, totals, quantities, counts, ratings - goes
        in a Markdown table: a header row, one item per row, each number in its own column, at most
        five columns. Never write such lists as sentences or bullets.
      - A single key figure ("total revenue is **12,345.00**") goes in bold.
      - Lists without numbers are short bullet lists. Use bold for names the user asked about.
      - No headings, no horizontal rules, no code blocks unless the user asks for JSON.
      """;

  static readonly JsonSerializerOptions _traceJson = new(JsonSerializerDefaults.Web);

  public async Task<ChatReply> AskAsync(IReadOnlyList<ChatTurn> turns, string? bearerToken, CancellationToken cancellationToken)
  {
    await using var session = await ConnectMcpAsync(bearerToken, cancellationToken);

    var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };
    foreach (var turn in turns.TakeLast(MaxHistoryTurns))
    {
      var role = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? ChatRole.Assistant : ChatRole.User;
      messages.Add(new ChatMessage(role, turn.Content));
    }

    var chatOptions = new ChatOptions
    {
      Tools = [.. session.Tools],
      Temperature = _options.Temperature,
      // OllamaSharp copies its own request type through; Think is the reasoning toggle.
      RawRepresentationFactory = _ => new OllamaSharp.Models.Chat.ChatRequest { Think = _options.Think }
    };

    var stopwatch = Stopwatch.StartNew();
    ChatResponse response;
    try
    {
      response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
    }
    catch (HttpRequestException ex)
    {
      throw new ChatUnavailableException(
          $"Could not reach Ollama at {_options.Endpoint} ({ex.Message}). Start Ollama, then pull the model with: ollama pull {_options.Model}", ex);
    }
    catch (OllamaException ex)
    {
      throw new ChatUnavailableException(
          $"Ollama rejected the request: {ex.Message}. If the model is missing, run: ollama pull {_options.Model}", ex);
    }
    stopwatch.Stop();

    var toolCalls = CollectToolCalls(response);
    _logger.LogInformation("Answered in {ElapsedMs} ms with {ToolCallCount} tool call(s) using {Model}",
        stopwatch.ElapsedMilliseconds, toolCalls.Count, _options.Model);

    return new ChatReply(FinalText(response), toolCalls, response.ModelId ?? _options.Model, stopwatch.ElapsedMilliseconds);
  }

  // Never throws: the page renders whatever is reachable and explains the rest.
  public async Task<ChatStatus> GetStatusAsync(string? bearerToken, CancellationToken cancellationToken)
  {
    var reachable = false;
    var modelAvailable = false;
    try
    {
      reachable = await _ollama.IsRunningAsync(cancellationToken);
      if (reachable)
      {
        var models = await _ollama.ListLocalModelsAsync(cancellationToken);
        modelAvailable = models.Any(model => _options.Matches(model.Name));
      }
    }
    catch (Exception ex) when (ex is HttpRequestException or OllamaException or TaskCanceledException)
    {
      _logger.LogWarning(ex, "Ollama status check failed for {Endpoint}", _options.Endpoint);
    }

    IReadOnlyList<string> toolNames = [];
    string? mcpError = null;
    try
    {
      await using var session = await ConnectMcpAsync(bearerToken, cancellationToken);
      toolNames = [.. session.Tools.Select(tool => tool.Name)];
    }
    catch (ChatUnavailableException ex)
    {
      mcpError = ex.Message;
    }

    return new ChatStatus(reachable, modelAvailable, _options.Model, _options.Endpoint.ToString(), toolNames, mcpError);
  }

  async Task<McpSession> ConnectMcpAsync(string? bearerToken, CancellationToken cancellationToken)
  {
    try
    {
      return await _mcp.ConnectAsync(bearerToken, cancellationToken);
    }
    catch (Exception ex) when (ex is HttpRequestException or McpException or IOException or InvalidOperationException)
    {
      throw new ChatUnavailableException($"Could not reach the MCP server at {_mcp.Endpoint} ({ex.Message}).", ex);
    }
  }

  // The pipeline appends every intermediate message (tool calls, tool results, the final
  // answer) to the response; pair calls with results by id for the trace.
  static List<ToolCallTrace> CollectToolCalls(ChatResponse response)
  {
    var calls = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);
    var traces = new List<ToolCallTrace>();
    foreach (var content in response.Messages.SelectMany(message => message.Contents))
    {
      switch (content)
      {
        case FunctionCallContent call:
          calls[call.CallId] = call;
          break;
        case FunctionResultContent result:
          var name = calls.TryGetValue(result.CallId, out var call2) ? call2.Name : "(unknown)";
          var arguments = calls.TryGetValue(result.CallId, out var call3) && call3.Arguments is { Count: > 0 }
              ? JsonSerializer.Serialize(call3.Arguments, _traceJson)
              : "{}";
          traces.Add(new ToolCallTrace(name, arguments, Preview(result.Result)));
          break;
      }
    }

    return traces;
  }

  static string Preview(object? result)
  {
    var text = result switch
    {
      null => "",
      string s => s,
      JsonElement element => element.ToString(),
      _ => JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions)
    };
    return text.Length <= ResultPreviewLength ? text : string.Concat(text.AsSpan(0, ResultPreviewLength - 1), "…");
  }

  // The last assistant message's text, minus any reasoning content (a separate content type,
  // so it is already excluded from Text). Models sometimes narrate before a tool call; that
  // narration lives in earlier messages and is left out on purpose.
  static string FinalText(ChatResponse response)
  {
    var final = response.Messages.LastOrDefault(message => message.Role == ChatRole.Assistant)?.Text;
    return string.IsNullOrWhiteSpace(final) ? response.Text.Trim() : final.Trim();
  }
}
