namespace BikeBuilder.API.Notifications.Email;

public interface IEmailSender
{
  // Throws when the provider rejects the message: the Service Bus trigger then abandons the
  // message so it is redelivered and eventually dead-lettered, which is the retry story.
  Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
