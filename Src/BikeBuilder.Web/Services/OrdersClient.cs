using System.Text.Json;

namespace BikeBuilder.Web.Services;

// Reads the Orders microservice's GraphQL endpoint with a plain authorized HttpClient -
// one back-office query doesn't warrant a generated client, and the shape mirrors the
// RatingsClient REST pattern the rest of this app uses.
public class OrdersClient(HttpClient http)
{
  const string OrdersQuery = """
      query Orders {
        orders {
          id
          customerName
          customerEmail
          status
          createdAt
          placedAt
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

  public async Task<List<OrderDto>> ListAsync(CancellationToken cancellationToken = default)
  {
    var response = await http.PostAsJsonAsync("/graphql", new { query = OrdersQuery }, _jsonOptions, cancellationToken);
    response.EnsureSuccessStatusCode();

    var payload = await response.Content.ReadFromJsonAsync<GraphQLResponse>(_jsonOptions, cancellationToken)
        ?? throw new InvalidOperationException("Empty GraphQL response.");

    if (payload.Errors is { Count: > 0 } errors)
      throw new InvalidOperationException(errors[0].Message);

    return payload.Data?.Orders ?? [];
  }

  sealed record GraphQLResponse(GraphQLData? Data, List<GraphQLError>? Errors);
  sealed record GraphQLData(List<OrderDto> Orders);
  sealed record GraphQLError(string Message);
}

public sealed record OrderDto(
    int Id,
    string CustomerName,
    string? CustomerEmail,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PlacedAt,
    decimal Total,
    List<OrderItemDto> Items);

public sealed record OrderItemDto(
    string ProductType,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);
