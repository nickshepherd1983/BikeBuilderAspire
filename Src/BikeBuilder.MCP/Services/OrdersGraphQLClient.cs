namespace BikeBuilder.MCP.Services;

// Reads the orders service's GraphQL endpoint with a plain HttpClient - the same three
// back-office documents the admin app's OrdersClient uses, plus a single-order lookup.
public class OrdersGraphQLClient(HttpClient _http)
{
  const string OrderFields = """
      id
      customerName
      customerEmail
      customerPhone
      status
      createdAt
      placedAt
      subtotal
      shippingCost
      shippingMethod
      total
      shippingAddress {
        fullName
        line1
        line2
        city
        state
        postalCode
        country
      }
      payment {
        summary
      }
      items {
        productType
        productId
        productName
        unitPrice
        quantity
        lineTotal
      }
      """;

  // Placed orders, out of SQL. The service caps this at the 100 most recent.
  static readonly string OrdersQuery = $$"""
      query Orders {
        orders {
          {{OrderFields}}
        }
      }
      """;

  static readonly string OrderQuery = $$"""
      query Order($id: Int!) {
        order(id: $id) {
          {{OrderFields}}
        }
      }
      """;

  // Carts still being filled in, out of Redis; expiresAt is when the cart's TTL runs out.
  const string DraftOrdersQuery = """
      query DraftOrders {
        draftOrders {
          id
          customerName
          customerEmail
          createdAt
          expiresAt
          total
          items {
            productType
            productId
            productName
            unitPrice
            quantity
            lineTotal
          }
        }
      }
      """;

  static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

  public async Task<List<OrderDto>> ListOrdersAsync(CancellationToken cancellationToken) =>
      (await ExecuteAsync<OrdersData>(OrdersQuery, null, cancellationToken))?.Orders ?? [];

  public async Task<OrderDto?> GetOrderAsync(int id, CancellationToken cancellationToken) =>
      (await ExecuteAsync<OrderData>(OrderQuery, new { id }, cancellationToken))?.Order;

  public async Task<List<DraftOrderDto>> ListDraftOrdersAsync(CancellationToken cancellationToken) =>
      (await ExecuteAsync<DraftOrdersData>(DraftOrdersQuery, null, cancellationToken))?.DraftOrders ?? [];

  async Task<TData?> ExecuteAsync<TData>(string query, object? variables, CancellationToken cancellationToken) where TData : class
  {
    // Relative path: the base address is the service root, so this appends /graphql.
    var response = await _http.PostAsJsonAsync("graphql", new { query, variables }, _jsonOptions, cancellationToken);
    response.EnsureSuccessStatusCode();

    var payload = await response.Content.ReadFromJsonAsync<GraphQLResponse<TData>>(_jsonOptions, cancellationToken)
        ?? throw new McpException("The orders service returned an empty response.");

    if (payload.Errors is { Count: > 0 } errors)
    {
      // HotChocolate answers 200 with an AUTH_* error code when the caller lacks the role (or
      // sent no token at all). Say what is needed rather than surfacing the raw error.
      if (errors.Any(error => error.Code?.StartsWith("AUTH_", StringComparison.Ordinal) == true))
      {
        throw new McpException(
            "Order data requires a signed-in user with the OrderViewer or Admin role. " +
            "No such token accompanied this request, so orders cannot be listed.");
      }

      throw new McpException($"The orders service reported an error: {errors[0].Message}");
    }

    return payload.Data;
  }

  sealed record GraphQLResponse<TData>(TData? Data, List<GraphQLError>? Errors);
  sealed record OrdersData(List<OrderDto> Orders);
  sealed record OrderData(OrderDto? Order);
  sealed record DraftOrdersData(List<DraftOrderDto> DraftOrders);

  sealed record GraphQLError(string Message, Dictionary<string, JsonElement>? Extensions)
  {
    public string? Code =>
        Extensions is not null && Extensions.TryGetValue("code", out var code) && code.ValueKind == JsonValueKind.String
            ? code.GetString()
            : null;
  }
}

// Total is what the shopper paid: Subtotal (the items) plus ShippingCost.
public sealed record OrderDto(
    int Id,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PlacedAt,
    decimal Subtotal,
    decimal ShippingCost,
    string ShippingMethod,
    decimal Total,
    AddressDto ShippingAddress,
    PaymentDto Payment,
    List<OrderItemDto> Items);

public sealed record AddressDto(
    string FullName,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country);

// The orders service keeps only a display summary of the card ("Visa •••• 4242").
public sealed record PaymentDto(string Summary);

// Ids are Guids here, not ints: drafts live in Redis and get their id there, and an order is
// renumbered by SQL's identity column when it's finally placed.
public sealed record DraftOrderDto(
    Guid Id,
    string CustomerName,
    string? CustomerEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    decimal Total,
    List<OrderItemDto> Items);

// ProductId identifies the catalog product - a component or a bike build, which one being said
// by the product type. Name and price are the snapshot taken when the item joined the cart.
public sealed record OrderItemDto(
    string ProductType,
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);
