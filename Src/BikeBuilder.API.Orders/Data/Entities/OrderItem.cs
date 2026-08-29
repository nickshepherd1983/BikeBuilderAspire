namespace BikeBuilder.API.Orders.Data.Entities;

public class OrderItem
{
  public int Id { get; set; }
  public int OrderId { get; set; }
  public ProductType ProductType { get; set; }
  // The product's id in the catalog bounded context (Component.Id or BikeBuild.Id).
  public int ProductId { get; set; }
  // Snapshotted at add-to-order time: the catalog can rename/reprice later without
  // rewriting history in this bounded context.
  public required string ProductName { get; set; }
  public decimal UnitPrice { get; set; }
  public int Quantity { get; set; }

  public decimal LineTotal => UnitPrice * Quantity;
}

public enum ProductType
{
  Component,
  BikeBuild
}
