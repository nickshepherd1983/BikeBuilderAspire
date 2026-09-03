using System.Net;
using System.Text;

namespace BikeBuilder.API.Notifications.Email;

// Renders the receipt. Pure: event in, message out, so it is trivially testable and the
// Function stays a thin adapter. Invariant "$0.00" formatting matches the order toast (and
// the integration test asserts on the text body); every string field is HTML-encoded because
// product names and addresses are user- or catalog-supplied.
public static class OrderConfirmationEmailBuilder
{
  public static EmailMessage Build(OrderConfirmationRequestedEvent order) => new(
      To: order.CustomerEmail,
      ToName: order.CustomerName,
      Subject: $"Your BikeBuilder order #{order.OrderId}",
      TextBody: BuildText(order),
      HtmlBody: BuildHtml(order),
      CustomId: $"order-{order.OrderId}");

  static string BuildText(OrderConfirmationRequestedEvent order)
  {
    var text = new StringBuilder();
    text.AppendLine(CultureInfo.InvariantCulture, $"Thanks, {order.CustomerName}!");
    text.AppendLine();
    text.AppendLine(CultureInfo.InvariantCulture, $"Your order #{order.OrderId} is confirmed (placed {FormatDate(order.PlacedAt)}).");
    text.AppendLine();
    foreach (var item in order.Items)
      text.AppendLine(CultureInfo.InvariantCulture, $"  {item.Quantity} x {item.ProductName}  {Money(item.LineTotal)}");
    text.AppendLine();
    text.AppendLine(CultureInfo.InvariantCulture, $"Subtotal: {Money(order.Subtotal)}");
    text.AppendLine(CultureInfo.InvariantCulture, $"Shipping ({order.ShippingMethod}): {Money(order.ShippingCost)}");
    text.AppendLine(CultureInfo.InvariantCulture, $"Total: {Money(order.Total)}");
    text.AppendLine();
    text.AppendLine("Shipping to:");
    foreach (var line in AddressLines(order.ShippingAddress))
      text.AppendLine(CultureInfo.InvariantCulture, $"  {line}");
    text.AppendLine();
    text.AppendLine(CultureInfo.InvariantCulture, $"Paid with {order.PaymentSummary}.");
    return text.ToString();
  }

  static string BuildHtml(OrderConfirmationRequestedEvent order)
  {
    var html = new StringBuilder();
    html.Append("<div style=\"font-family:Segoe UI,Helvetica,Arial,sans-serif;max-width:600px;color:#222\">");
    html.Append(CultureInfo.InvariantCulture, $"<h2 style=\"margin:0 0 8px\">Thanks, {Encode(order.CustomerName)}!</h2>");
    html.Append(CultureInfo.InvariantCulture,
        $"<p style=\"margin:0 0 16px\">Your order <b>#{order.OrderId}</b> is confirmed (placed {Encode(FormatDate(order.PlacedAt))}).</p>");
    html.Append("<table style=\"width:100%;border-collapse:collapse\">");
    foreach (var item in order.Items)
    {
      html.Append(CultureInfo.InvariantCulture,
          $"<tr><td style=\"padding:4px 0;border-bottom:1px solid #eee\">{item.Quantity} &times; {Encode(item.ProductName)}</td>" +
          $"<td style=\"padding:4px 0;border-bottom:1px solid #eee;text-align:right\">{Money(item.LineTotal)}</td></tr>");
    }
    html.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"padding:8px 0 0\">Subtotal</td><td style=\"text-align:right;padding:8px 0 0\">{Money(order.Subtotal)}</td></tr>");
    html.Append(CultureInfo.InvariantCulture, $"<tr><td>Shipping ({Encode(order.ShippingMethod)})</td><td style=\"text-align:right\">{Money(order.ShippingCost)}</td></tr>");
    html.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"font-weight:bold\">Total</td><td style=\"text-align:right;font-weight:bold\">{Money(order.Total)}</td></tr>");
    html.Append("</table>");
    html.Append("<p style=\"margin:16px 0 4px;font-weight:bold\">Shipping to</p><p style=\"margin:0\">");
    html.Append(string.Join("<br/>", AddressLines(order.ShippingAddress).Select(Encode)));
    html.Append("</p>");
    html.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:16px 0 0;color:#666\">Paid with {Encode(order.PaymentSummary)}.</p>");
    html.Append("</div>");
    return html.ToString();
  }

  static IEnumerable<string> AddressLines(OrderConfirmationAddress address)
  {
    yield return address.FullName;
    yield return address.Line1;
    if (!string.IsNullOrWhiteSpace(address.Line2))
      yield return address.Line2;
    yield return $"{address.City}, {address.State} {address.PostalCode}";
    yield return address.Country;
  }

  static string Money(decimal amount) => "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);

  static string FormatDate(DateTimeOffset placedAt) => placedAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

  static string Encode(string value) => WebUtility.HtmlEncode(value);
}
