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
    return new OrderList(orders.Count, [.. orders.Take(ToolSupport.PageSize(take)).Select(ToView)], ScopeNote);
  }

  [McpServerTool(Name = "get_order", ReadOnly = true, Idempotent = true),
   Description("Gets one placed order by its integer id, with customer details and every line item.")]
  public async Task<OrderView> GetOrder(
      [Description("The order id.")] int id,
      CancellationToken cancellationToken = default) =>
      ToView(await _orders.GetOrderAsync(id, cancellationToken) ?? throw new McpException($"Order {id} was not found."));

  [McpServerTool(Name = "list_draft_orders", ReadOnly = true, Idempotent = true),
   Description("Lists draft orders: carts shoppers are still filling in, which expire an hour after their last change. Each has a Guid id, customer, expiry time, total and line items.")]
  public async Task<List<DraftOrderView>> ListDraftOrders(CancellationToken cancellationToken = default) =>
      [.. (await _orders.ListDraftOrdersAsync(cancellationToken)).Select(ToView)];

  [McpServerTool(Name = "orders_summary", ReadOnly = true, Idempotent = true),
   Description("Summarises placed orders: order count, total revenue, average order value, date range, the top products by quantity and by revenue, and the top customers by spend. Use this for any question about totals, revenue or best sellers.")]
  public async Task<OrdersSummary> OrdersSummary(CancellationToken cancellationToken = default)
  {
    var orders = await _orders.ListOrdersAsync(cancellationToken);
    if (orders.Count == 0)
      return new OrdersSummary(0, ToolSupport.Money(0m), ToolSupport.Money(0m), null, null, [], [], [], ScopeNote);

    var revenue = orders.Sum(order => order.Total);
    var productSales = orders
        .SelectMany(order => order.Items)
        .GroupBy(item => (item.ProductType, item.ProductId, item.ProductName))
        .Select(group => (
            group.Key.ProductType,
            group.Key.ProductId,
            group.Key.ProductName,
            Quantity: group.Sum(item => item.Quantity),
            Revenue: group.Sum(item => item.LineTotal)))
        .ToList();
    var customerSales = orders
        .GroupBy(order => order.CustomerName)
        .Select(group => (CustomerName: group.Key, OrderCount: group.Count(), TotalSpent: group.Sum(order => order.Total)))
        .ToList();

    return new OrdersSummary(
        orders.Count,
        ToolSupport.Money(revenue),
        ToolSupport.Money(decimal.Round(revenue / orders.Count, 2)),
        ToolSupport.Date(orders.Min(order => order.CreatedAt)),
        ToolSupport.Date(orders.Max(order => order.CreatedAt)),
        [.. productSales.OrderByDescending(sales => sales.Quantity).ThenByDescending(sales => sales.Revenue).Take(TopProductCount)
            .Select(sales => new ProductSales(sales.ProductType, sales.ProductId, sales.ProductName, sales.Quantity, ToolSupport.Money(sales.Revenue)))],
        [.. productSales.OrderByDescending(sales => sales.Revenue).Take(TopProductCount)
            .Select(sales => new ProductSales(sales.ProductType, sales.ProductId, sales.ProductName, sales.Quantity, ToolSupport.Money(sales.Revenue)))],
        [.. customerSales.OrderByDescending(sales => sales.TotalSpent).Take(TopCustomerCount)
            .Select(sales => new CustomerSales(sales.CustomerName, sales.OrderCount, ToolSupport.Money(sales.TotalSpent)))],
        ScopeNote);
  }

  static OrderView ToView(OrderDto order) => new(
      order.Id,
      order.CustomerName,
      order.CustomerEmail,
      order.Status,
      ToolSupport.Date(order.CreatedAt),
      ToolSupport.Date(order.PlacedAt),
      ToolSupport.Money(order.Total),
      [.. order.Items.Select(ToView)]);

  static DraftOrderView ToView(DraftOrderDto draft) => new(
      draft.Id,
      draft.CustomerName,
      draft.CustomerEmail,
      ToolSupport.Date(draft.CreatedAt),
      ToolSupport.Date(draft.ExpiresAt),
      ToolSupport.Money(draft.Total),
      [.. draft.Items.Select(ToView)]);

  static OrderItemView ToView(OrderItemDto item) => new(
      item.ProductType,
      item.ProductId,
      item.ProductName,
      ToolSupport.Money(item.UnitPrice),
      item.Quantity,
      ToolSupport.Money(item.LineTotal));
}

// Money and dates are pre-formatted strings ($1,234.56 and MM/dd/yyyy HH:mm UTC) - see ToolSupport.
public sealed record OrderView(
    int Id,
    string CustomerName,
    string? CustomerEmail,
    string Status,
    string CreatedAt,
    string? PlacedAt,
    string Total,
    IReadOnlyList<OrderItemView> Items);

public sealed record DraftOrderView(
    Guid Id,
    string CustomerName,
    string? CustomerEmail,
    string CreatedAt,
    string ExpiresAt,
    string Total,
    IReadOnlyList<OrderItemView> Items);

public sealed record OrderItemView(string ProductType, int ProductId, string ProductName, string UnitPrice, int Quantity, string LineTotal);

public sealed record OrderList(int AvailableCount, IReadOnlyList<OrderView> Orders, string Note);

public sealed record ProductSales(string ProductType, int ProductId, string ProductName, int Quantity, string Revenue);

public sealed record CustomerSales(string CustomerName, int OrderCount, string TotalSpent);

public sealed record OrdersSummary(
    int OrderCount,
    string TotalRevenue,
    string AverageOrderValue,
    string? EarliestOrder,
    string? LatestOrder,
    IReadOnlyList<ProductSales> TopProductsByQuantity,
    IReadOnlyList<ProductSales> TopProductsByRevenue,
    IReadOnlyList<CustomerSales> TopCustomers,
    string Note);
