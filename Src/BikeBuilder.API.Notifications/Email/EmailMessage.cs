namespace BikeBuilder.API.Notifications.Email;

// Provider-neutral outbound email. CustomId is the caller's correlation handle (Mailjet echoes
// it back in its event log; the SMTP sender stamps it on a header) - "order-123" for receipts.
public sealed record EmailMessage(
    string To, string ToName, string Subject, string TextBody, string HtmlBody, string CustomId);
