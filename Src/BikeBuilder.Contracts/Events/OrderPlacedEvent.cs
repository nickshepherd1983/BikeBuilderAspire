namespace BikeBuilder.Contracts.Events;

public sealed record OrderPlacedEvent
{
  public required int OrderId { get; init; }
  public required string CustomerName { get; init; }
  public required decimal Total { get; init; }
  public required int ItemCount { get; init; }
  public required DateTimeOffset CreatedAt { get; init; }
}
