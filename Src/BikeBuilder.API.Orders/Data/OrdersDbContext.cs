namespace BikeBuilder.API.Orders.Data;

public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
  public DbSet<Order> Orders => Set<Order>();
  public DbSet<OrderItem> OrderItems => Set<OrderItem>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Order>(order =>
    {
      order.Property(o => o.CustomerName).HasMaxLength(200);
      order.Property(o => o.CustomerEmail).HasMaxLength(320);
      // Stored as a string for the same readability reason as Component.Manufacturer.
      order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
      order.Property(o => o.RowVersion).IsRowVersion();
      order.Ignore(o => o.Total);
      order.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<OrderItem>(item =>
    {
      item.Property(i => i.ProductType).HasConversion<string>().HasMaxLength(20);
      item.Property(i => i.ProductName).HasMaxLength(200);
      item.Property(i => i.UnitPrice).HasPrecision(18, 2);
      item.Ignore(i => i.LineTotal);
    });
  }
}
