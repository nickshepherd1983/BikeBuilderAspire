using HotChocolate;
using HotChocolate.Types;

namespace BikeBuilder.API.Orders.GraphQL;

[MutationType]
public static class Mutation
{
  public static async Task<Order> CreateOrder(string customerName, string? customerEmail,
      OrdersDbContext db, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(customerName))
      throw Error("Customer name is required.", "CUSTOMER_NAME_REQUIRED");

    var order = new Order
    {
      CustomerName = customerName.Trim(),
      CustomerEmail = string.IsNullOrWhiteSpace(customerEmail) ? null : customerEmail.Trim()
    };
    db.Orders.Add(order);
    await db.SaveChangesAsync(cancellationToken);
    return order;
  }

  public static async Task<Order> AddOrderItem(int orderId, ProductType productType, int productId, int quantity,
      OrdersDbContext db, [Service] CatalogPricingService catalog, CancellationToken cancellationToken)
  {
    var order = await GetDraftOrderAsync(orderId, db, cancellationToken);
    var (name, unitPrice) = await catalog.GetProductAsync(productType, productId, cancellationToken);

    // Adding the same product again bumps the quantity instead of duplicating the line.
    var existing = order.Items.Find(i => i.ProductType == productType && i.ProductId == productId);
    if (existing is not null)
    {
      existing.Quantity += Math.Max(1, quantity);
    }
    else
    {
      order.Items.Add(new OrderItem
      {
        ProductType = productType,
        ProductId = productId,
        ProductName = name,
        UnitPrice = unitPrice,
        Quantity = Math.Max(1, quantity)
      });
    }

    await db.SaveChangesAsync(cancellationToken);
    return order;
  }

  public static async Task<Order> RemoveOrderItem(int orderId, int orderItemId,
      OrdersDbContext db, CancellationToken cancellationToken)
  {
    var order = await GetDraftOrderAsync(orderId, db, cancellationToken);

    var item = order.Items.Find(i => i.Id == orderItemId)
        ?? throw Error($"Order item {orderItemId} was not found on order {orderId}.", "ORDER_ITEM_NOT_FOUND");

    order.Items.Remove(item);
    await db.SaveChangesAsync(cancellationToken);
    return order;
  }

  public static async Task<Order> ProcessOrder(int orderId,
      OrdersDbContext db, [Service] IEventPublisher eventPublisher, CancellationToken cancellationToken)
  {
    var order = await GetDraftOrderAsync(orderId, db, cancellationToken);

    if (order.Items.Count == 0)
      throw Error("An order needs at least one item before it can be processed.", "ORDER_EMPTY");

    order.Status = OrderStatus.Placed;
    order.PlacedAt = DateTimeOffset.UtcNow;
    // The rowversion turns a concurrent double-process into a concurrency exception rather
    // than a double-publish.
    await db.SaveChangesAsync(cancellationToken);

    await eventPublisher.PublishAsync(ServiceBusMessageTypes.OrderPlaced, new OrderPlacedEvent
    {
      OrderId = order.Id,
      CustomerName = order.CustomerName,
      Total = order.Total,
      ItemCount = order.Items.Sum(i => i.Quantity),
      CreatedAt = order.PlacedAt.Value
    }, cancellationToken);

    return order;
  }

  static async Task<Order> GetDraftOrderAsync(int orderId, OrdersDbContext db, CancellationToken cancellationToken)
  {
    var order = await db.Orders.Include(o => o.Items)
        .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
        ?? throw Error($"Order {orderId} was not found.", "ORDER_NOT_FOUND");

    if (order.Status != OrderStatus.Draft)
      throw Error($"Order {orderId} has already been placed.", "ORDER_ALREADY_PLACED");

    return order;
  }

  static GraphQLException Error(string message, string code) =>
      new(ErrorBuilder.New().SetMessage(message).SetCode(code).Build());
}
