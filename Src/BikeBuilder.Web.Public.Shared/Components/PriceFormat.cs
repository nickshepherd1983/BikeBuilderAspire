namespace BikeBuilder.Web.Public.Components;

public static class PriceFormat
{
  // Invariant "$" formatting keeps prices identical across machines (the integration test
  // asserts on cart and checkout totals).
  public static string Format(decimal value) => $"${value.ToString("N2", CultureInfo.InvariantCulture)}";
}
