namespace BikeBuilder.API.Orders.Data.Entities;

// The shopper's pick at checkout. Prices, names and delivery estimates for each live in
// ShippingOptions - the order stores the method plus the price it was quoted, so a later
// price change doesn't rewrite history.
public enum ShippingMethod
{
  Standard,
  Express,
  Overnight
}
