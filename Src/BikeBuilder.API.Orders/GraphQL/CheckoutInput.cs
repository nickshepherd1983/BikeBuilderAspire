namespace BikeBuilder.API.Orders.GraphQL;

// Everything the checkout page collects, sent in one go with processOrder. HotChocolate
// infers the input object types from these records; StrawberryShake generates matching
// records on the storefront side (records.inputs in .graphqlrc.json).
public sealed record CheckoutInput(
    string CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    AddressInput ShippingAddress,
    // Null means "same as shipping" - the storefront's default, so the common case sends one
    // address, not two identical ones.
    AddressInput? BillingAddress,
    ShippingMethod ShippingMethod,
    CardInput Card);

public sealed record AddressInput(
    string FullName,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country);

// The full card details travel here and no further: FakeCardProcessor turns them into a
// PaymentCard summary and the number and CVC are dropped.
public sealed record CardInput(
    string Number,
    string CardholderName,
    int ExpiryMonth,
    int ExpiryYear,
    string Cvc);
