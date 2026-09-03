using Microsoft.Extensions.Configuration;

namespace BikeBuilder.API.Notifications.Email;

// Which provider sends, and as whom. Selection is by configuration presence, the same way
// BikeBuilder.API picks its IUserDirectory: Mailjet when Email:Mailjet:ApiKey is set (the
// deployed Function App), SMTP when Email:Smtp:Host is set (the AppHost points it at the
// smtp4dev container), and nothing at all otherwise - the worker still starts and logs each
// dropped message. Empty strings count as absent so the Bicep can pass '' defaults.
public sealed class EmailOptions
{
  public const string DefaultFromAddress = "orders@bikebuilder.local";
  public const string DefaultFromName = "BikeBuilder";

  public string FromAddress { get; init; } = DefaultFromAddress;
  public string FromName { get; init; } = DefaultFromName;
  public SmtpSettings? Smtp { get; init; }
  public MailjetSettings? Mailjet { get; init; }

  public static EmailOptions FromConfiguration(IConfiguration configuration) => new()
  {
    FromAddress = configuration["Email:From:Address"] is { Length: > 0 } address ? address : DefaultFromAddress,
    FromName = configuration["Email:From:Name"] is { Length: > 0 } name ? name : DefaultFromName,
    Smtp = configuration["Email:Smtp:Host"] is { Length: > 0 } host
        ? new SmtpSettings(host, configuration.GetValue("Email:Smtp:Port", 25))
        : null,
    Mailjet = configuration["Email:Mailjet:ApiKey"] is { Length: > 0 } apiKey
        ? new MailjetSettings(apiKey, configuration["Email:Mailjet:SecretKey"] ?? string.Empty)
        : null
  };

  public sealed record SmtpSettings(string Host, int Port);

  public sealed record MailjetSettings(string ApiKey, string SecretKey);
}
