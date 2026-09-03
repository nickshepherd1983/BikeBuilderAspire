namespace BikeBuilder.Contracts.Events;

public sealed record OrderPlacedEvent
{
  public required int OrderId { get; init; }
  public required string CustomerName { get; init; }
  // What the shopper was charged: items plus shipping.
  public required decimal Total { get; init; }
  public required int ItemCount { get; init; }
  public required DateTimeOffset CreatedAt { get; init; }
  // Checkout details. Only the parts a downstream consumer (fulfilment, notifications) needs
  // to describe the order travel here - the full addresses stay in the orders database, and
  // the payment is the display summary ("Visa •••• 4242") that a card summary reduces to.
  public decimal Subtotal { get; init; }
  public decimal ShippingCost { get; init; }
  public string ShippingMethod { get; init; } = string.Empty;
  public string ShipToCity { get; init; } = string.Empty;
  public string ShipToState { get; init; } = string.Empty;
  public string ShipToCountry { get; init; } = string.Empty;
  public string PaymentSummary { get; init; } = string.Empty;
}
