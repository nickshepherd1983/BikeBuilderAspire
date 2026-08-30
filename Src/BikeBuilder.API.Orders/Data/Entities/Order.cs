namespace BikeBuilder.API.Orders.Data.Entities;

public class Order
{
  public int Id { get; set; }
  // Guest checkout: purchases are tied to a typed-in name, not a user account.
  public required string CustomerName { get; set; }
  public string? CustomerEmail { get; set; }
  public OrderStatus Status { get; set; } = OrderStatus.Draft;
  public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
  public DateTimeOffset? PlacedAt { get; set; }
  // Guards concurrent double-processing of the same draft order. Not part of the schema.
  [GraphQLIgnore]
  public byte[] RowVersion { get; set; } = [];
  public List<OrderItem> Items { get; } = [];

  // Derived, never stored - same convention as BikeBuild totals in the catalog context.
  public decimal Total => Items.Sum(i => i.UnitPrice * i.Quantity);
}

public enum OrderStatus
{
  Draft,
  Placed
}
