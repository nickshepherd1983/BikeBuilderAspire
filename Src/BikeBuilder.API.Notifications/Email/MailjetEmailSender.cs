using System.Net.Http.Json;
using System.Text.Json;

namespace BikeBuilder.API.Notifications.Email;

// Mailjet Send API v3.1 over the factory HttpClient registered in Program.cs (base address
// and Basic auth header set there). No SDK: the request is one JSON POST, and the official
// package drags in Newtonsoft for nothing. The standard resilience handler never retries a
// POST, so a failed send surfaces here and Service Bus redelivery is the only retry.
public sealed class MailjetEmailSender(HttpClient http, EmailOptions options, ILogger<MailjetEmailSender> logger) : IEmailSender
{
  static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

  public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
  {
    var request = new SendRequest([
      new SendMessage(
          new Party(options.FromAddress, options.FromName),
          [new Party(message.To, message.ToName)],
          message.Subject, message.TextBody, message.HtmlBody, message.CustomId)
    ]);

    using var response = await http.PostAsJsonAsync("send", request, cancellationToken);
    var body = await response.Content.ReadAsStringAsync(cancellationToken);
    if (!response.IsSuccessStatusCode)
      throw new InvalidOperationException($"Mailjet returned {(int)response.StatusCode} for {message.CustomId}: {body}");

    // A 200 can still carry a per-message "error" status (unknown sender, bad recipient).
    var result = JsonSerializer.Deserialize<SendResponse>(body, ResponseJson);
    var failed = result?.Messages?.FirstOrDefault(m => !string.Equals(m.Status, "success", StringComparison.OrdinalIgnoreCase));
    if (result?.Messages is not { Count: > 0 } || failed is not null)
      throw new InvalidOperationException($"Mailjet did not accept {message.CustomId} ({failed?.Status ?? "no result"}): {body}");

    logger.LogInformation("Sent {CustomId} to {To} via Mailjet", message.CustomId, message.To);
  }

  // Property names are Mailjet's wire format (PascalCase, "HTMLPart", "CustomID"); the default
  // serializer keeps them as declared.
  sealed record SendRequest(IReadOnlyList<SendMessage> Messages);

  sealed record SendMessage(Party From, IReadOnlyList<Party> To, string Subject, string TextPart, string HTMLPart, string CustomID);

  sealed record Party(string Email, string Name);

  sealed record SendResponse(List<SendResult>? Messages);

  sealed record SendResult(string? Status);
}
