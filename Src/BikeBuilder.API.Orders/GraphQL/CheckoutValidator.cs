namespace BikeBuilder.API.Orders.GraphQL;

// Server-side checks on the checkout form, independent of the MudForm validation the
// storefront runs: the GraphQL endpoint is anonymous and reachable without the UI. Returns a
// trimmed copy so the mutation stores exactly what was validated.
static class CheckoutValidator
{
  public static CheckoutInput Validate(CheckoutInput checkout)
  {
    var name = Required(checkout.CustomerName, "Your name", Order.CustomerNameMaxLength);
    var email = Optional(checkout.CustomerEmail, "Email", Order.CustomerEmailMaxLength);
    if (email is not null && !System.Net.Mail.MailAddress.TryCreate(email, out _))
      throw new CheckoutException("CHECKOUT_INVALID", "Enter a valid email address.");
    var phone = Optional(checkout.CustomerPhone, "Phone", Order.CustomerPhoneMaxLength);

    return checkout with
    {
      CustomerName = name,
      CustomerEmail = email,
      CustomerPhone = phone,
      ShippingAddress = Validate(checkout.ShippingAddress, "Shipping"),
      BillingAddress = checkout.BillingAddress is null ? null : Validate(checkout.BillingAddress, "Billing")
    };
  }

  static AddressInput Validate(AddressInput address, string label) => new(
      Required(address.FullName, $"{label} name", Address.NameMaxLength),
      Required(address.Line1, $"{label} address", Address.LineMaxLength),
      Optional(address.Line2, $"{label} address line 2", Address.LineMaxLength),
      Required(address.City, $"{label} city", Address.CityMaxLength),
      Required(address.State, $"{label} state", Address.StateMaxLength),
      Required(address.PostalCode, $"{label} postal code", Address.PostalCodeMaxLength),
      Required(address.Country, $"{label} country", Address.CountryMaxLength));

  static string Required(string? value, string field, int maxLength) =>
      Optional(value, field, maxLength)
      ?? throw new CheckoutException("CHECKOUT_INVALID", $"{field} is required.");

  static string? Optional(string? value, string field, int maxLength)
  {
    if (string.IsNullOrWhiteSpace(value))
      return null;

    var trimmed = value.Trim();
    if (trimmed.Length > maxLength)
      throw new CheckoutException("CHECKOUT_INVALID", $"{field} can't be longer than {maxLength} characters.");

    return trimmed;
  }
}
