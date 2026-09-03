namespace BikeBuilder.API.Notifications.Email;

// What runs when neither Email:Smtp:Host nor Email:Mailjet:ApiKey is configured. Completing
// the message (rather than throwing) is deliberate: a misconfigured host should not pile up
// dead letters, and the warning per message is the visible signal in the dashboard.
public sealed class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
  public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
  {
    logger.LogWarning("No email provider configured (Email:Smtp:Host or Email:Mailjet:ApiKey); dropping \"{Subject}\" to {To}",
        message.Subject, message.To);
    return Task.CompletedTask;
  }
}
