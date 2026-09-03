namespace BikeBuilder.Contracts.Events;

// Everything the confirmation email needs, so the consumer (BikeBuilder.API.Notifications)
// never has to read the orders database. Deliberately separate from OrderPlacedEvent, which
// stays the lean "something happened" broadcast: this one carries the customer's email
// address and the full line items, and only travels on the order-emails queue.
public sealed record OrderConfirmationRequestedEvent
{
  public required int OrderId { get; init; }
  public required string CustomerName { get; init; }
  public required string CustomerEmail { get; init; }
  public required DateTimeOffset PlacedAt { get; init; }
  public required IReadOnlyList<OrderConfirmationItem> Items { get; init; }
  public required decimal Subtotal { get; init; }
  // Display name of the shipping choice ("Express"), not the enum.
  public required string ShippingMethod { get; init; }
  public required decimal ShippingCost { get; init; }
  public required decimal Total { get; init; }
  public required OrderConfirmationAddress ShippingAddress { get; init; }
  // The card summary as shown to the shopper ("Visa •••• 4242") - never the number.
  public required string PaymentSummary { get; init; }
}

public sealed record OrderConfirmationItem(string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record OrderConfirmationAddress(
    string FullName, string Line1, string? Line2, string City, string State, string PostalCode, string Country);
