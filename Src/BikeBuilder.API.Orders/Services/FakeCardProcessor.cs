using System.Globalization;

namespace BikeBuilder.API.Orders.Services;

// A checkout that can't be accepted. Code is the GraphQL error code the mutation surfaces
// (CARD_INVALID, CARD_DECLINED, CHECKOUT_INVALID...); the message is safe to show a shopper.
public sealed class CheckoutException(string code, string message) : Exception(message)
{
  public string Code { get; } = code;
}

// Stands in for a payment gateway. It checks what a gateway would check before talking to
// the network (format, Luhn, expiry, CVC), decides the outcome deterministically so demos and
// tests can rely on it, and hands back only the summary a gateway would: brand, last four,
// expiry. Nothing here persists or logs the full number.
public static class FakeCardProcessor
{
  // Stripe's convention: this test number is always declined, everything else that passes
  // Luhn is approved. Documented in the README as the way to demo a failed payment.
  const string DeclinedSuffix = "0002";

  public static PaymentCard Authorize(string number, string cardholderName, int expiryMonth, int expiryYear, string cvc, DateOnly today)
  {
    // Shoppers type numbers with spaces or dashes; those are the only non-digits allowed.
    var digits = new string([.. (number ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-')]);
    if (digits.Length is < 13 or > 19 || !digits.All(char.IsAsciiDigit))
      throw new CheckoutException("CARD_INVALID", "Enter a valid card number.");
    if (!PassesLuhn(digits))
      throw new CheckoutException("CARD_INVALID", "That card number isn't valid.");

    if (string.IsNullOrWhiteSpace(cardholderName))
      throw new CheckoutException("CARD_INVALID", "Enter the name on the card.");
    if (cardholderName.Trim().Length > PaymentCard.CardholderNameMaxLength)
      throw new CheckoutException("CARD_INVALID", $"The name on the card can't be longer than {PaymentCard.CardholderNameMaxLength} characters.");

    // Two-digit years are what the MM/YY field on the checkout page naturally produces.
    if (expiryYear < 100)
      expiryYear += 2000;
    if (expiryMonth is < 1 or > 12 || expiryYear is < 2000 or > 2100)
      throw new CheckoutException("CARD_INVALID", "Enter a valid expiry date.");
    // A card is good through the last day of its expiry month.
    if (new DateOnly(expiryYear, expiryMonth, 1).AddMonths(1) <= today)
      throw new CheckoutException("CARD_EXPIRED", "That card has expired.");

    var cvcDigits = (cvc ?? string.Empty).Trim();
    if (cvcDigits.Length is < 3 or > 4 || !cvcDigits.All(char.IsAsciiDigit))
      throw new CheckoutException("CARD_INVALID", "Enter the 3 or 4 digit security code.");

    if (digits.EndsWith(DeclinedSuffix, StringComparison.Ordinal))
      throw new CheckoutException("CARD_DECLINED", "Your card was declined. Try a different card.");

    return new PaymentCard
    {
      Brand = DetectBrand(digits),
      Last4 = digits[^4..],
      ExpiryMonth = expiryMonth,
      ExpiryYear = expiryYear,
      CardholderName = cardholderName.Trim()
    };
  }

  static bool PassesLuhn(string digits)
  {
    var sum = 0;
    var doubleIt = false;
    for (var i = digits.Length - 1; i >= 0; i--)
    {
      var digit = digits[i] - '0';
      if (doubleIt)
      {
        digit *= 2;
        if (digit > 9)
          digit -= 9;
      }
      sum += digit;
      doubleIt = !doubleIt;
    }
    return sum % 10 == 0;
  }

  // The common issuer prefixes; anything else is just "Card". Purely cosmetic.
  static string DetectBrand(string digits)
  {
    if (digits.StartsWith('4'))
      return "Visa";
    if (digits.StartsWith("34", StringComparison.Ordinal) || digits.StartsWith("37", StringComparison.Ordinal))
      return "Amex";
    if (digits.StartsWith("6011", StringComparison.Ordinal) || digits.StartsWith("65", StringComparison.Ordinal))
      return "Discover";

    var prefix2 = int.Parse(digits[..2], CultureInfo.InvariantCulture);
    var prefix4 = int.Parse(digits[..4], CultureInfo.InvariantCulture);
    if (prefix2 is >= 51 and <= 55 || prefix4 is >= 2221 and <= 2720)
      return "Mastercard";

    return "Card";
  }
}
