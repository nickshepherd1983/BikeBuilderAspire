using MailKit.Security;
using MimeKit;

namespace BikeBuilder.API.Notifications.Email;

// Plain, unauthenticated SMTP - which is only ever the smtp4dev catcher the AppHost runs.
// A real mail relay would need TLS and credentials; that path is Mailjet's, not this one.
public sealed class SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
  readonly EmailOptions.SmtpSettings _smtp = options.Smtp
      ?? throw new InvalidOperationException("SmtpEmailSender requires Email:Smtp:Host.");

  public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
  {
    var mime = new MimeMessage();
    mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
    mime.To.Add(new MailboxAddress(message.ToName, message.To));
    mime.Subject = message.Subject;
    mime.Headers.Add("X-BikeBuilder-Custom-Id", message.CustomId);
    mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

    // One connection per message: receipts are rare, and a pooled session would only add a
    // reconnect path to get wrong.
    using var client = new MailKit.Net.Smtp.SmtpClient();
    await client.ConnectAsync(_smtp.Host, _smtp.Port, SecureSocketOptions.None, cancellationToken);
    await client.SendAsync(mime, cancellationToken);
    await client.DisconnectAsync(quit: true, cancellationToken);

    logger.LogInformation("Sent {CustomId} to {To} via SMTP {Host}:{Port}", message.CustomId, message.To, _smtp.Host, _smtp.Port);
  }
}
