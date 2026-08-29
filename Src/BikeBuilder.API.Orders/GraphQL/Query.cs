using HotChocolate.Authorization;
using HotChocolate.Types;

namespace BikeBuilder.API.Orders.GraphQL;

[QueryType]
public static class Query
{
  public static async Task<Order?> GetOrder(int id, OrdersDbContext db, CancellationToken cancellationToken) =>
      await db.Orders.Include(o => o.Items).AsNoTracking()
          .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

  // Back-office view for the signed-in web app: order lists carry customer names/emails,
  // so unlike the guest-checkout mutations this requires a JWT.
  [Authorize]
  public static async Task<List<Order>> GetOrders(OrdersDbContext db, CancellationToken cancellationToken) =>
      await db.Orders.Include(o => o.Items).AsNoTracking()
          .OrderByDescending(o => o.CreatedAt)
          .Take(100)
          .ToListAsync(cancellationToken);
}
