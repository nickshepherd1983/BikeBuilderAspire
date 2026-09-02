namespace BikeBuilder.API.Chat.Services;

// Wire shapes shared with the admin app's ChatClient. The client keeps the transcript and
// sends it whole each time - the service holds no conversation state.
public sealed record ChatTurn(string Role, string Content)
{
  public bool IsAssistant => string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase);
}

public sealed record AskRequest(List<ChatTurn> Messages);

public sealed record ToolCallTrace(string Name, string Arguments, string ResultPreview);

public sealed record ChatReply(string Reply, IReadOnlyList<ToolCallTrace> ToolCalls, string Model, long ElapsedMs);

public sealed record ChatStatus(
    bool OllamaReachable,
    bool ModelAvailable,
    string Model,
    string Endpoint,
    IReadOnlyList<string> ToolNames,
    string? McpError);

// The model host or the MCP server can't be reached, or the model isn't installed: a 503 with
// a message that names the fix, rather than a bare failure.
public sealed class ChatUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
