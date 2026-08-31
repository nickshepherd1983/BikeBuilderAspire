namespace BikeBuilder.API.Orders.Data;

// An unsubmitted guest cart. Deliberately NOT an EF entity: drafts live only in Redis under
// a sliding one-hour TTL, and never reach SQL until they're processed - at which point
// Mutation.ProcessOrder copies them into a real Order/OrderItem pair.
//
// Ids are Guids rather than the ints the SQL entities use. The two stores allocate ids
// independently, so an order's id changes when it's placed; sharing an int space would mean
// either coordinating a counter with SQL's identity column or risking collisions. The
// storefront only holds the draft id (in localStorage) and drops it on checkout, so the
// change of id is invisible to it.
//
// The shape mirrors Order/OrderItem so both read identically to GraphQL clients.
public sealed class DraftOrder
{
  public required Guid Id { get; init; }
  // Guest checkout: purchases are tied to a typed-in name, not a user account.
  public required string CustomerName { get; set; }
  public string? CustomerEmail { get; set; }
  public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
  // When the cart's Redis key currently expires. Recomputed on every save (the TTL slides),
  // and surfaced so the back office can show a countdown.
  public DateTimeOffset ExpiresAt { get; set; }
  public List<DraftOrderItem> Items { get; init; } = [];

  // Derived, never stored - same convention as Order.Total.
  public decimal Total => Items.Sum(i => i.UnitPrice * i.Quantity);
}

public sealed class DraftOrderItem
{
  public required Guid Id { get; init; }
  public ProductType ProductType { get; init; }
  // The product's id in the catalog bounded context (Component.Id or BikeBuild.Id).
  public int ProductId { get; init; }
  // Snapshotted at add-to-cart time, exactly as OrderItem does, so the price the shopper saw
  // survives a catalog repricing mid-session.
  public required string ProductName { get; init; }
  public decimal UnitPrice { get; init; }
  public int Quantity { get; set; }

  public decimal LineTotal => UnitPrice * Quantity;
}
