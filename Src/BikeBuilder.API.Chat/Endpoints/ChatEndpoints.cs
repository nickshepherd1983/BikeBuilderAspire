namespace BikeBuilder.API.Chat.Endpoints;

// The assistant's HTTP surface. Admin-only end to end: the page, its nav link and these
// endpoints all gate on the same policy. The caller's bearer token is passed down so the MCP
// server can forward it to the role-gated orders queries.
public static class ChatEndpoints
{
  const int MaxTurns = 40;
  const int MaxTurnLength = 4000;

  public static void MapChatEndpoints(this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/chat")
        .RequireAuthorization(Policies.AdminOnly)
        .RequireCors("BlazorWasmClient");

    // What the page shows in its banner: is the model host up, is the model pulled, which
    // tools the MCP server offers. Never fails - unreachable parts are reported, not thrown.
    group.MapGet("/status", async (ChatService chat, HttpContext http, CancellationToken ct) =>
        Results.Ok(await chat.GetStatusAsync(BearerToken(http), ct)));

    group.MapPost("", async (AskRequest request, ChatService chat, HttpContext http, CancellationToken ct) =>
    {
      if (request.Messages is not { Count: > 0 and <= MaxTurns })
        return Results.BadRequest($"messages must hold between 1 and {MaxTurns} turns.");
      if (request.Messages.Any(turn => string.IsNullOrWhiteSpace(turn.Content) || turn.Content.Length > MaxTurnLength))
        return Results.BadRequest($"Each turn needs content of at most {MaxTurnLength} characters.");
      if (!string.Equals(request.Messages[^1].Role, "user", StringComparison.OrdinalIgnoreCase))
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
