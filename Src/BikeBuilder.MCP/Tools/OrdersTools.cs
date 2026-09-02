namespace BikeBuilder.MCP.Tools;

// Order tools over the orders service's GraphQL endpoint. The back-office queries there are
// role-gated, and the caller's token is forwarded (BearerForwardingHandler), so an anonymous
// MCP client gets a "sign in required" answer from these rather than data.
[McpServerToolType]
public sealed class OrdersTools(OrdersGraphQLClient _orders)
{
  const int TopProductCount = 10;
  const int TopCustomerCount = 5;
  const string ScopeNote = "Based on the 100 most recent placed orders the orders service exposes.";

  [McpServerTool(Name = "list_orders", ReadOnly = true, Idempotent = true),
   Description("Lists placed customer orders, newest first, with customer, status, timestamps, total and line items. Only the 100 most recent orders are available; use orders_summary for totals and best sellers instead of adding these up.")]
  public async Task<OrderList> ListOrders(
      [Description("How many of the most recent orders to return, 1 to 50.")] int take = ToolSupport.DefaultPageSize,
      CancellationToken cancellationToken = default)
  {
    var orders = await _orders.ListOrdersAsync(cancellationToken);
    return new OrderList(orders.Count, [.. orders.Take(ToolSupport.PageSize(take))], ScopeNote);
  }

  [McpServerTool(Name = "get_order", ReadOnly = true, Idempotent = true),
   Description("Gets one placed order by its integer id, with customer details and every line item.")]
  public async Task<OrderDto> GetOrder(
      [Description("The order id.")] int id,
      CancellationToken cancellationToken = default) =>
      await _orders.GetOrderAsync(id, cancellationToken) ?? throw new McpException($"Order {id} was not found.");

  [McpServerTool(Name = "list_draft_orders", ReadOnly = true, Idempotent = true),
   Description("Lists draft orders: carts shoppers are still filling in, which expire an hour after their last change. Each has a Guid id, customer, expiry time, total and line items.")]
  public async Task<List<DraftOrderDto>> ListDraftOrders(CancellationToken cancellationToken = default) =>
      await _orders.ListDraftOrdersAsync(cancellationToken);

  [McpServerTool(Name = "orders_summary", ReadOnly = true, Idempotent = true),
   Description("Summarises placed orders: order count, total revenue, average order value, date range, the top products by quantity and by revenue, and the top customers by spend. Use this for any question about totals, revenue or best sellers.")]
  public async Task<OrdersSummary> OrdersSummary(CancellationToken cancellationToken = default)
  {
    var orders = await _orders.ListOrdersAsync(cancellationToken);
    if (orders.Count == 0)
      return new OrdersSummary(0, 0, 0, null, null, [], [], [], ScopeNote);

    var revenue = orders.Sum(order => order.Total);
    var productSales = orders
        .SelectMany(order => order.Items)
        .GroupBy(item => (item.ProductType, item.ProductId, item.ProductName))
        .Select(group => new ProductSales(
            group.Key.ProductType,
            group.Key.ProductId,
            group.Key.ProductName,
            group.Sum(item => item.Quantity),
            group.Sum(item => item.LineTotal)))
        .ToList();
    var customerSales = orders
        .GroupBy(order => order.CustomerName)
        .Select(group => new CustomerSales(group.Key, group.Count(), group.Sum(order => order.Total)))
        .ToList();

    return new OrdersSummary(
        orders.Count,
        revenue,
        decimal.Round(revenue / orders.Count, 2),
        orders.Min(order => order.CreatedAt),
        orders.Max(order => order.CreatedAt),
        [.. productSales.OrderByDescending(sales => sales.Quantity).ThenByDescending(sales => sales.Revenue).Take(TopProductCount)],
        [.. productSales.OrderByDescending(sales => sales.Revenue).Take(TopProductCount)],
        [.. customerSales.OrderByDescending(sales => sales.TotalSpent).Take(TopCustomerCount)],
        ScopeNote);
  }
}

public sealed record OrderList(int AvailableCount, IReadOnlyList<OrderDto> Orders, string Note);

public sealed record ProductSales(string ProductType, int ProductId, string ProductName, int Quantity, decimal Revenue);

public sealed record CustomerSales(string CustomerName, int OrderCount, decimal TotalSpent);

public sealed record OrdersSummary(
    int OrderCount,
    decimal TotalRevenue,
    decimal AverageOrderValue,
    DateTimeOffset? EarliestOrder,
    DateTimeOffset? LatestOrder,
    IReadOnlyList<ProductSales> TopProductsByQuantity,
    IReadOnlyList<ProductSales> TopProductsByRevenue,
    IReadOnlyList<CustomerSales> TopCustomers,
    string Note);
