using HotChocolate.Authorization;

namespace BikeBuilder.API.Orders.GraphQL;

[QueryType]
public static class Query
{
  // SQL holds only placed orders now - unsubmitted carts live in Redis and are reached
  // through the draft fields below.
  public static async Task<Order?> GetOrder(int id, OrdersDbContext db, CancellationToken cancellationToken) =>
      await db.Orders.Include(o => o.Items).AsNoTracking()
          .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

  // Back-office view for the signed-in web app: order lists carry customer names/emails,
  // so unlike the guest-checkout mutations this requires a JWT with an order-viewing role.
  [Authorize(Policies.ViewOrders)]
  public static async Task<List<Order>> GetOrders(OrdersDbContext db, CancellationToken cancellationToken) =>
      await db.Orders.Include(o => o.Items).AsNoTracking()
          .OrderByDescending(o => o.CreatedAt)
          .Take(100)
          .ToListAsync(cancellationToken);

  // Anonymous, like the rest of guest checkout: this is how the storefront resumes the cart
  // whose id it kept in localStorage. A null answer means the cart's hour ran out.
  public static Task<DraftOrder?> GetDraftOrder(Guid id, DraftOrderStore store) =>
      store.GetAsync(id);

  // The back office's "in process" view. Carries customer names/emails just like GetOrders,
  // so it takes the same JWT and role.
  [Authorize(Policies.ViewOrders)]
  public static Task<List<DraftOrder>> GetDraftOrders(DraftOrderStore store) =>
      store.ListAsync();

  // Anonymous: the checkout page renders its shipping choices from this so the prices shown
  // and the price charged (ProcessOrder reads the same list) can't drift apart.
  public static IReadOnlyList<ShippingOption> GetShippingOptions() => ShippingOptions.All;
}
