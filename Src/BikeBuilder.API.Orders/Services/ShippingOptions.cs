namespace BikeBuilder.API.Orders.Services;

// One shipping choice as the storefront presents it. Days are business days.
public sealed record ShippingOption(
    ShippingMethod Method,
    string Name,
    string Description,
    decimal Price,
    int MinDays,
    int MaxDays);

// The single source of truth for what shipping costs. The storefront reads this list over
// GraphQL to render the radio buttons, and ProcessOrder prices the order from it again -
// the client only ever sends the method, never the cost, so a tampered request can't ship
// for free.
public static class ShippingOptions
{
  public static readonly IReadOnlyList<ShippingOption> All =
  [
    new(ShippingMethod.Standard, "Standard", "Ground shipping", 9.99m, 5, 7),
    new(ShippingMethod.Express, "Express", "Priority shipping", 24.99m, 2, 3),
    new(ShippingMethod.Overnight, "Overnight", "Next business day", 49.99m, 1, 1)
  ];

  public static ShippingOption Get(ShippingMethod method) =>
      All.FirstOrDefault(option => option.Method == method)
      ?? throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown shipping method.");
}
