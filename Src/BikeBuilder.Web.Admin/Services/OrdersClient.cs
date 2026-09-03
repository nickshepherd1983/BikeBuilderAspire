using System.Text.Json;

namespace BikeBuilder.Web.Admin.Services;

// Reads the Orders microservice's GraphQL endpoint with a plain authorized HttpClient -
// two back-office queries don't warrant a generated client, and the shape mirrors the
// RatingsClient REST pattern the rest of this app uses.
public class OrdersClient(HttpClient http)
{
  // Placed orders, out of SQL. Ship-to, shipping and the card summary are checkout-time
  // snapshots; the billing address exists too but the table has no room for it.
  const string OrdersQuery = """
      query Orders {
        orders {
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
            expiryMonth
            expiryYear
          }
          items {
            productType
            productName
            unitPrice
            quantity
            lineTotal
          }
        }
      }
      """;

  // Carts still being filled in, out of Redis. expiresAt is when the cart's TTL runs out -
  // there's no equivalent on a placed order, which is why these don't share a query.
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
            productName
            unitPrice
            quantity
            lineTotal
          }
        }
      }
      """;

  static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

  public async Task<List<OrderDto>> ListAsync(CancellationToken cancellationToken = default) =>
      (await ExecuteAsync<OrdersData>(OrdersQuery, cancellationToken))?.Orders ?? [];

  public async Task<List<DraftOrderDto>> ListDraftsAsync(CancellationToken cancellationToken = default) =>
      (await ExecuteAsync<DraftOrdersData>(DraftOrdersQuery, cancellationToken))?.DraftOrders ?? [];

  async Task<TData?> ExecuteAsync<TData>(string query, CancellationToken cancellationToken) where TData : class
  {
    // Relative path: the base address carries the gateway's /orders prefix, and a rooted
    // "/graphql" would replace it rather than append to it.
    var response = await http.PostAsJsonAsync("graphql", new { query }, _jsonOptions, cancellationToken);
    response.EnsureSuccessStatusCode();

    var payload = await response.Content.ReadFromJsonAsync<GraphQLResponse<TData>>(_jsonOptions, cancellationToken)
        ?? throw new InvalidOperationException("Empty GraphQL response.");

    if (payload.Errors is { Count: > 0 } errors)
      throw new InvalidOperationException(errors[0].Message);

    return payload.Data;
  }

  sealed record GraphQLResponse<TData>(TData? Data, List<GraphQLError>? Errors);
  sealed record OrdersData(List<OrderDto> Orders);
  sealed record DraftOrdersData(List<DraftOrderDto> DraftOrders);
  sealed record GraphQLError(string Message);
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

// Summary is the "Visa •••• 4242" line the orders service builds; nothing else about the
// card is available, by design.
public sealed record PaymentDto(string Summary, int ExpiryMonth, int ExpiryYear);

// Ids are Guids here, not ints: drafts live in Redis and get their id there, and an order
// is renumbered by SQL's identity column when it's finally placed.
public sealed record DraftOrderDto(
    Guid Id,
    string CustomerName,
    string? CustomerEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    decimal Total,
    List<OrderItemDto> Items);

public sealed record OrderItemDto(
    string ProductType,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);
