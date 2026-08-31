namespace BikeBuilder.API.Orders.GraphQL;

// Guest checkout builds a cart in Redis (DraftOrderStore) and only writes to SQL at the
// moment it's processed - see DraftOrder for why the two stores use different id types.
[MutationType]
public static class Mutation
{
  // No CancellationToken on the draft paths: StackExchange.Redis' async API takes
  // CommandFlags rather than a token, so there'd be nothing to pass it to.
  public static async Task<DraftOrder> CreateOrder(string customerName, string? customerEmail,
      DraftOrderStore store)
  {
    if (string.IsNullOrWhiteSpace(customerName))
      throw Error("Customer name is required.", "CUSTOMER_NAME_REQUIRED");

    var draft = new DraftOrder
    {
      Id = Guid.NewGuid(),
      CustomerName = customerName.Trim(),
      CustomerEmail = string.IsNullOrWhiteSpace(customerEmail) ? null : customerEmail.Trim()
    };
    await store.SaveAsync(draft);
    return draft;
  }

  public static async Task<DraftOrder> AddOrderItem(Guid orderId, ProductType productType, int productId, int quantity,
      DraftOrderStore store, [Service] CatalogPricingService catalog, CancellationToken cancellationToken)
  {
    var draft = await GetDraftAsync(orderId, store);
    var (name, unitPrice) = await catalog.GetProductAsync(productType, productId, cancellationToken);

    // Adding the same product again bumps the quantity instead of duplicating the line.
    var existing = draft.Items.Find(i => i.ProductType == productType && i.ProductId == productId);
    if (existing is not null)
    {
      existing.Quantity += Math.Max(1, quantity);
    }
    else
    {
      draft.Items.Add(new DraftOrderItem
      {
        Id = Guid.NewGuid(),
        ProductType = productType,
        ProductId = productId,
        ProductName = name,
        UnitPrice = unitPrice,
        Quantity = Math.Max(1, quantity)
      });
    }

    // Also slides the cart's TTL out to a fresh hour.
    await store.SaveAsync(draft);
    return draft;
  }

  public static async Task<DraftOrder> RemoveOrderItem(Guid orderId, Guid orderItemId,
      DraftOrderStore store)
  {
    var draft = await GetDraftAsync(orderId, store);

    var item = draft.Items.Find(i => i.Id == orderItemId)
        ?? throw Error($"Order item {orderItemId} was not found on order {orderId}.", "ORDER_ITEM_NOT_FOUND");

    draft.Items.Remove(item);
    await store.SaveAsync(draft);
    return draft;
  }

  public static async Task<Order> ProcessOrder(Guid orderId,
      DraftOrderStore store, OrdersDbContext db, [Service] IEventPublisher eventPublisher,
      CancellationToken cancellationToken)
  {
    // Claim rather than read: GETDEL hands the cart to exactly one caller, which is what
    // stops a concurrent double-process from placing the order twice. (The old SQL draft row
    // relied on its rowversion token for the same guarantee.)
    var draft = await store.ClaimAsync(orderId)
        ?? throw Error($"Order {orderId} was not found.", "ORDER_NOT_FOUND");

    if (draft.Items.Count == 0)
    {
      // Put it back - an empty cart is a shopper error, not a processed order, and losing
      // their cart over it would be rude.
      await store.SaveAsync(draft);
      throw Error("An order needs at least one item before it can be processed.", "ORDER_EMPTY");
    }

    // The draft's Guid stays behind in Redis; the placed order gets SQL's identity id, and
    // that's the id the back office and the OrderPlaced event use from here on.
    var order = new Order
    {
      CustomerName = draft.CustomerName,
      CustomerEmail = draft.CustomerEmail,
      CreatedAt = draft.CreatedAt,
      Status = OrderStatus.Placed,
      PlacedAt = DateTimeOffset.UtcNow
    };
    foreach (var item in draft.Items)
    {
      order.Items.Add(new OrderItem
      {
        ProductType = item.ProductType,
        ProductId = item.ProductId,
        ProductName = item.ProductName,
        UnitPrice = item.UnitPrice,
        Quantity = item.Quantity
      });
    }

    db.Orders.Add(order);
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

  // A missing key is indistinguishable from an expired one, so both surface as ORDER_NOT_FOUND.
  static async Task<DraftOrder> GetDraftAsync(Guid orderId, DraftOrderStore store) =>
      await store.GetAsync(orderId)
      ?? throw Error($"Order {orderId} was not found.", "ORDER_NOT_FOUND");

  static GraphQLException Error(string message, string code) =>
      new(ErrorBuilder.New().SetMessage(message).SetCode(code).Build());
}
