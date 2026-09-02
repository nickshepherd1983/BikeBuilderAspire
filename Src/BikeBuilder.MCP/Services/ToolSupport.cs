namespace BikeBuilder.MCP.Services;

// Shared clamping, parsing and trimming for the tool classes. Results feed a language model
// with a finite context, so pages are capped and long free text is cut short.
public static class ToolSupport
{
  public const int MaxPageSize = 50;
  public const int DefaultPageSize = 20;
  public const int DescriptionLength = 200;

  public static int Page(int page) => page < 1 ? 1 : page;

  public static int PageSize(int pageSize) => pageSize switch
  {
    < 1 => DefaultPageSize,
    > MaxPageSize => MaxPageSize,
    _ => pageSize
  };

  // The API serializes costs and totals as invariant-culture decimal strings on the wire.
  public static decimal ParseMoney(string value) =>
      decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

  // Tool results are read by a language model that copies values verbatim into its answer,
  // so money and dates leave here already in the shape the answer should show them: dollars
  // with two decimals and thousands separators, and MM/dd/yyyy HH:mm in UTC.
  public static string Money(decimal amount) => amount.ToString("$#,##0.00", CultureInfo.InvariantCulture);

  public static string Money(string wireAmount) => Money(ParseMoney(wireAmount));

  public static string Date(DateTimeOffset value) => value.ToUniversalTime().ToString("MM/dd/yyyy HH:mm", CultureInfo.InvariantCulture);

  public static string? Date(DateTimeOffset? value) => value is null ? null : Date(value.Value);

  public static string Trim(string text, int maxLength = DescriptionLength)
  {
    if (string.IsNullOrWhiteSpace(text))
      return "";

    var collapsed = text.Trim();
    return collapsed.Length <= maxLength ? collapsed : string.Concat(collapsed.AsSpan(0, maxLength - 1), "…");
  }

  // Polymorphic ComponentInformation travels as a JSON string ("" when the component has
  // none); hand the model the parsed object rather than a string it would have to unescape.
  public static JsonElement? ParseJson(string json) =>
      string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json).RootElement.Clone();
}
