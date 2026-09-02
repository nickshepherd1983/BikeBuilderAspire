namespace BikeBuilder.API.Chat.Endpoints;

// The assistant's HTTP surface. The chat widget and these endpoints gate on the same policy
// (the Assistant role, or Admin). The caller's bearer token is passed down so the MCP server
// can forward it to the role-gated orders queries - an Assistant-only user gets the "needs
// OrderViewer" answer from those tools rather than the data.
public static class ChatEndpoints
{
  const int MaxTurns = 40;
  // The user's questions are typed; answers carry tables and run long. Both bounds are
  // abuse guards only - ChatService trims history to what the model should see.
  const int MaxUserTurnLength = 4000;
  const int MaxAssistantTurnLength = 40_000;

  public static void MapChatEndpoints(this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/chat")
        .RequireAuthorization(Policies.UseAssistant)
        .RequireCors("BlazorWasmClient");

    // What the page shows in its banner: is the model host up, is the model pulled, which
    // tools the MCP server offers. Never fails - unreachable parts are reported, not thrown.
    group.MapGet("/status", async (ChatService chat, HttpContext http, CancellationToken ct) =>
        Results.Ok(await chat.GetStatusAsync(BearerToken(http), ct)));

    group.MapPost("", async (AskRequest request, ChatService chat, HttpContext http, CancellationToken ct) =>
    {
      if (request.Messages is not { Count: > 0 and <= MaxTurns })
        return Results.BadRequest($"messages must hold between 1 and {MaxTurns} turns.");
      if (request.Messages.Any(turn => string.IsNullOrWhiteSpace(turn.Content)))
        return Results.BadRequest("Every turn needs some content.");
      if (request.Messages.Any(turn => turn.Content.Length > (turn.IsAssistant ? MaxAssistantTurnLength : MaxUserTurnLength)))
        return Results.BadRequest($"A question can be at most {MaxUserTurnLength} characters and a previous answer at most {MaxAssistantTurnLength}.");
      if (request.Messages[^1].IsAssistant)
        return Results.BadRequest("The last turn must be the user's question.");

      try
      {
        return Results.Ok(await chat.AskAsync(request.Messages, BearerToken(http), ct));
      }
      catch (ChatUnavailableException ex)
      {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Assistant unavailable");
      }
    });
  }

  static string? BearerToken(HttpContext http)
  {
    var header = http.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
  }
}
