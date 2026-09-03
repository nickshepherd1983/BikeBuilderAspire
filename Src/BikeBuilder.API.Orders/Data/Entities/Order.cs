namespace BikeBuilder.API.Orders.Data.Entities;

public class Order
{
  public const int CustomerNameMaxLength = 200;
  public const int CustomerEmailMaxLength = 320;
  public const int CustomerPhoneMaxLength = 30;

  public int Id { get; set; }
  // Guest checkout: purchases are tied to a typed-in name, not a user account.
  public required string CustomerName { get; set; }
  public string? CustomerEmail { get; set; }
  public string? CustomerPhone { get; set; }
  public OrderStatus Status { get; set; } = OrderStatus.Draft;
  public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
  public DateTimeOffset? PlacedAt { get; set; }
  // Checkout details, all captured the moment the order is placed. Ship-to and bill-to are
  // separate copies even when the shopper ticked "same as shipping" - each is its own owned
  // instance, and a later edit to one must not silently change the other.
  public required Address ShippingAddress { get; set; }
  public required Address BillingAddress { get; set; }
  public ShippingMethod ShippingMethod { get; set; }
  // The price quoted for the chosen method at checkout (see ShippingMethod).
  public decimal ShippingCost { get; set; }
  public required PaymentCard Payment { get; set; }
  // Guards concurrent double-processing of the same draft order. Not part of the schema.
  [GraphQLIgnore]
  public byte[] RowVersion { get; set; } = [];
  public List<OrderItem> Items { get; } = [];

  // Derived, never stored - same convention as BikeBuild totals in the catalog context.
  public decimal Subtotal => Items.Sum(i => i.UnitPrice * i.Quantity);
  // What the shopper is charged: items plus shipping. This is the figure the OrderPlaced
  // event and the back office call "total".
  public decimal Total => Subtotal + ShippingCost;
}

public enum OrderStatus
{
  Draft,
  Placed
}
