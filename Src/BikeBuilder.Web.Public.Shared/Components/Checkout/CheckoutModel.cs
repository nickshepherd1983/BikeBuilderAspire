using System.Text.RegularExpressions;

namespace BikeBuilder.Web.Public.Components.Checkout;

// The checkout form's state. Mutable classes because MudBlazor two-way binds to them; the
// generated CheckoutInput record is built from this on submit, and the card fields are the
// only place the full number ever exists on the client.
public sealed partial class CheckoutModel
{
  public string Name { get; set; } = string.Empty;
  public string Email { get; set; } = string.Empty;
  public string Phone { get; set; } = string.Empty;
  public AddressModel Shipping { get; } = new();
  public bool BillingSameAsShipping { get; set; } = true;
  public AddressModel Billing { get; } = new();
  public ShippingMethod ShippingMethod { get; set; } = ShippingMethod.Standard;
  public string CardNumber { get; set; } = string.Empty;
  public string CardholderName { get; set; } = string.Empty;
  // MM/YY as typed - see ValidateExpiry for the parse.
  public string CardExpiry { get; set; } = string.Empty;
  public string CardCvc { get; set; } = string.Empty;

  public CheckoutInput ToInput()
  {
    var (month, year) = ParseExpiry(CardExpiry);
    return new CheckoutInput
    {
      CustomerName = Name.Trim(),
      CustomerEmail = NullIfBlank(Email),
      CustomerPhone = NullIfBlank(Phone),
      ShippingAddress = Shipping.ToInput(),
      // Null tells the service "same as shipping" and saves sending the address twice.
      BillingAddress = BillingSameAsShipping ? null : Billing.ToInput(),
      ShippingMethod = ShippingMethod,
      Card = new CardInput
      {
        Number = CardNumber.Trim(),
        CardholderName = CardholderName.Trim(),
        ExpiryMonth = month,
        ExpiryYear = year,
        Cvc = CardCvc.Trim()
      }
    };
  }

  // Client-side checks mirror what the service enforces so most mistakes are caught before a
  // round trip; the service remains the authority (it also runs the Luhn check).
  public static string? ValidateEmail(string? value) =>
      string.IsNullOrWhiteSpace(value) || System.Net.Mail.MailAddress.TryCreate(value.Trim(), out _)
          ? null
          : "Enter a valid email address";

  public static string? ValidateCardNumber(string? value)
  {
    var digits = (value ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
    return digits.Length is >= 13 and <= 19 && digits.All(char.IsAsciiDigit) ? null : "Enter a 13 to 19 digit card number";
  }

  public static string? ValidateExpiry(string? value) =>
      ExpiryPattern().IsMatch(value ?? string.Empty) ? null : "Use MM/YY";

  public static string? ValidateCvc(string? value) =>
      CvcPattern().IsMatch(value ?? string.Empty) ? null : "3 or 4 digits";

  static (int Month, int Year) ParseExpiry(string value)
  {
    var parts = value.Trim().Split('/');
    return (int.Parse(parts[0], CultureInfo.InvariantCulture), 2000 + int.Parse(parts[1], CultureInfo.InvariantCulture));
  }

  static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

  [GeneratedRegex(@"^(0[1-9]|1[0-2])\s*/\s*\d{2}$")]
  private static partial Regex ExpiryPattern();

  [GeneratedRegex(@"^\d{3,4}$")]
  private static partial Regex CvcPattern();
}

public sealed class AddressModel
{
  public string FullName { get; set; } = string.Empty;
  public string Line1 { get; set; } = string.Empty;
  public string Line2 { get; set; } = string.Empty;
  public string City { get; set; } = string.Empty;
  public string State { get; set; } = string.Empty;
  public string PostalCode { get; set; } = string.Empty;
  // Prefilled: the store's prices are in dollars, so this is the overwhelming default.
  public string Country { get; set; } = "United States";

  public AddressInput ToInput() => new()
  {
    FullName = FullName.Trim(),
    Line1 = Line1.Trim(),
    Line2 = string.IsNullOrWhiteSpace(Line2) ? null : Line2.Trim(),
    City = City.Trim(),
    State = State.Trim(),
    PostalCode = PostalCode.Trim(),
    Country = Country.Trim()
  };
}
