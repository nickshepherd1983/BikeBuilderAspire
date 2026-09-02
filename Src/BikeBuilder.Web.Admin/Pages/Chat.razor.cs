using Microsoft.AspNetCore.Components.Web;

namespace BikeBuilder.Web.Admin.Pages;

// Sealed so the plain Dispose below is the whole story, as on InProcessOrders.
public sealed partial class Chat(ChatClient _chatClient, ISnackbar _snackbar) : IDisposable
{
  static readonly string[] Suggestions =
  [
    "Which bike build has the best average rating?",
    "What is the total revenue from placed orders, and what sells best?",
    "Which are the five most expensive components?",
    "Summarise the most recent orders."
  ];

  // Cancels an in-flight question when the user navigates away - a local model can take a
  // while, and nobody is left to read the answer.
  readonly CancellationTokenSource _cts = new();
  readonly List<ChatBubble> _transcript = [];

  ChatStatusDto? _status;
  bool _statusFailed;
  string _input = "";
  bool _busy;

  protected override Task OnInitializedAsync() => LoadStatusAsync();

  // The chat host starts after the services it depends on, so a page opened early can beat
  // it; the banner's Retry calls this again rather than making the user reload.
  async Task LoadStatusAsync()
  {
    try
    {
      _status = await _chatClient.GetStatusAsync(_cts.Token);
      _statusFailed = false;
    }
    catch (Exception ex) when (ex is HttpRequestException or AccessTokenNotAvailableException or ChatException)
    {
      _statusFailed = true;
    }
  }

  Task OnKeyUp(KeyboardEventArgs args) => args.Key == "Enter" ? SendAsync(_input) : Task.CompletedTask;

  async Task SendAsync(string question)
  {
    question = question.Trim();
    if (_busy || question.Length == 0)
      return;

    _input = "";
    _busy = true;
    _transcript.Add(ChatBubble.User(question));
    try
    {
      // Failed turns are shown but not resent - the model never saw them.
      var history = _transcript
          .Where(bubble => !bubble.IsError)
          .Select(bubble => new ChatTurnDto(bubble.Role, bubble.Content))
          .ToList();
      var reply = await _chatClient.AskAsync(history, _cts.Token);
      _transcript.Add(ChatBubble.Assistant(reply));
    }
    catch (OperationCanceledException)
    {
      // Navigated away.
    }
    catch (Exception ex) when (ex is HttpRequestException or AccessTokenNotAvailableException or ChatException)
    {
      _transcript.Add(ChatBubble.Error(ex.Message));
      _snackbar.Add(ex.Message, Severity.Error);
    }
    finally
    {
      _busy = false;
    }
  }

  void Clear() => _transcript.Clear();

  public void Dispose()
  {
    _cts.Cancel();
    _cts.Dispose();
  }

  sealed record ChatBubble(string Role, string Content, List<ToolCallDto> ToolCalls, string? Model, long ElapsedMs, bool IsError)
  {
    public bool IsUser => Role == "user";

    public static ChatBubble User(string content) => new("user", content, [], null, 0, false);

    public static ChatBubble Assistant(ChatReplyDto reply) => new("assistant", reply.Reply, reply.ToolCalls, reply.Model, reply.ElapsedMs, false);

    public static ChatBubble Error(string message) => new("assistant", message, [], null, 0, true);
  }
}
