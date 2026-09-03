using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BikeBuilder.API.Orders.Data;

public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
  public DbSet<Order> Orders => Set<Order>();
  public DbSet<OrderItem> OrderItems => Set<OrderItem>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Order>(order =>
    {
      order.Property(o => o.CustomerName).HasMaxLength(Order.CustomerNameMaxLength);
      order.Property(o => o.CustomerEmail).HasMaxLength(Order.CustomerEmailMaxLength);
      order.Property(o => o.CustomerPhone).HasMaxLength(Order.CustomerPhoneMaxLength);
      // Stored as a string for the same readability reason as Component.Manufacturer.
      order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
      order.Property(o => o.ShippingMethod).HasConversion<string>().HasMaxLength(20);
      order.Property(o => o.ShippingCost).HasPrecision(18, 2);
      order.Property(o => o.RowVersion).IsRowVersion();
      order.Ignore(o => o.Subtotal);
      order.Ignore(o => o.Total);
      // Owned types share the Orders table (ShippingAddress_City and so on) - an order's
      // addresses and card summary are read and written with it and never on their own, so a
      // separate table would only add joins. IsRequired on the navigation is what makes the
      // columns NOT NULL; without it EF treats a table-split dependent as optional.
      order.OwnsOne(o => o.ShippingAddress, ConfigureAddress);
      order.Navigation(o => o.ShippingAddress).IsRequired();
      order.OwnsOne(o => o.BillingAddress, ConfigureAddress);
      order.Navigation(o => o.BillingAddress).IsRequired();
      order.OwnsOne(o => o.Payment, payment =>
      {
        payment.Property(p => p.Brand).HasMaxLength(PaymentCard.BrandMaxLength);
        payment.Property(p => p.Last4).HasMaxLength(PaymentCard.Last4Length);
        payment.Property(p => p.CardholderName).HasMaxLength(PaymentCard.CardholderNameMaxLength);
        payment.Ignore(p => p.Summary);
      });
      order.Navigation(o => o.Payment).IsRequired();
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

  static void ConfigureAddress<TOwner>(OwnedNavigationBuilder<TOwner, Address> address) where TOwner : class
  {
    address.Property(a => a.FullName).HasMaxLength(Address.NameMaxLength);
    address.Property(a => a.Line1).HasMaxLength(Address.LineMaxLength);
    address.Property(a => a.Line2).HasMaxLength(Address.LineMaxLength);
    address.Property(a => a.City).HasMaxLength(Address.CityMaxLength);
    address.Property(a => a.State).HasMaxLength(Address.StateMaxLength);
    address.Property(a => a.PostalCode).HasMaxLength(Address.PostalCodeMaxLength);
    address.Property(a => a.Country).HasMaxLength(Address.CountryMaxLength);
  }
}
