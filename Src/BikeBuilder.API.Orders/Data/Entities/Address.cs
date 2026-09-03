namespace BikeBuilder.API.Orders.Data.Entities;

// A postal address snapshotted onto the order at checkout. Owned by Order (twice: ship-to and
// bill-to), so it has no id of its own and lives in the Orders table's own columns.
public class Address
{
  public const int NameMaxLength = 200;
  public const int LineMaxLength = 200;
  public const int CityMaxLength = 100;
  public const int StateMaxLength = 100;
  public const int PostalCodeMaxLength = 20;
  public const int CountryMaxLength = 60;

  public required string FullName { get; set; }
  public required string Line1 { get; set; }
  public string? Line2 { get; set; }
  public required string City { get; set; }
  public required string State { get; set; }
  public required string PostalCode { get; set; }
  public required string Country { get; set; }
}
