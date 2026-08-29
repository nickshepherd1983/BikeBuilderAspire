using HotChocolate.Types;

namespace BikeBuilder.API.Orders.GraphQL;

[QueryType]
public static class Query
{
  public static async Task<Order?> GetOrder(int id, OrdersDbContext db, CancellationToken cancellationToken) =>
      await db.Orders.Include(o => o.Items).AsNoTracking()
          .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
}
