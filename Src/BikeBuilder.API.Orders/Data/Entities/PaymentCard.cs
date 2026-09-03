namespace BikeBuilder.API.Orders.Data.Entities;

// What's kept of the card after the (fake) authorization: enough to show "Visa •••• 4242,
// expires 12/30" in the back office and nothing that could charge it again. The full number
// and the CVC never leave FakeCardProcessor - the same shape a real gateway hands back, so
// swapping in a real one later changes nothing downstream.
public class PaymentCard
{
  public const int BrandMaxLength = 20;
  public const int Last4Length = 4;
  public const int CardholderNameMaxLength = 200;

  public required string Brand { get; set; }
  public required string Last4 { get; set; }
  public int ExpiryMonth { get; set; }
  public int ExpiryYear { get; set; }
  public required string CardholderName { get; set; }

  // Derived, never stored - the one-line form every UI and the OrderPlaced event show.
  public string Summary => $"{Brand} •••• {Last4}";
}
