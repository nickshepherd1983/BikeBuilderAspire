using Microsoft.EntityFrameworkCore.Design;

namespace BikeBuilder.API.Orders.Data;

// `dotnet ef migrations add` tries the app's own service provider first, and that can't be
// built at design time: DraftOrderStore needs the Redis multiplexer Aspire only injects at run
// time, and the provider validates every registration in Development. This factory is what EF
// falls back to. No connection string is needed - adding or scripting a migration only has to
// know the provider is SQL Server.
public sealed class OrdersDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
  public OrdersDbContext CreateDbContext(string[] args) =>
      new(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlServer().Options);
}
