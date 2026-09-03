using System.Text.RegularExpressions;

namespace BikeBuilder.Test.Integration.PageObjects;

// Drives the storefront's /checkout page: contact, addresses, shipping choice, card, and the
// confirmation panel that replaces the form once the order is placed.
public class CheckoutPage(IPage page)
{
  // Stripe's well-known test numbers, which FakeCardProcessor honours: the first is always
  // approved, the second always declined.
  public const string ApprovedCard = "4242 4242 4242 4242";
  public const string DeclinedCard = "4000 0000 0000 0002";

  public async Task WaitForLoadedAsync()
  {
    await page.GetByRole(AriaRole.Heading, new() { Name = "Checkout", Exact = true }).WaitForAsync();
    // The form only renders once the draft has come back over GraphQL; the contact block's
    // name field is the first thing it shows.
    await page.GetByLabel("Your name").WaitForAsync(new() { Timeout = 30_000 });
  }

  // The billing block (same labels) is hidden while "same as shipping" is ticked, but scoping
  // to the shipping block keeps these locators unambiguous either way.
  ILocator ShippingBlock => page.Locator("#shipping-address");

  public ILocator Confirmation => page.Locator(".checkout-confirmation");

  public ILocator SummaryLine(string label) =>
      page.Locator("#order-summary").GetByText(new Regex($@"^{Regex.Escape(label)}: \$"));

  public async Task FillContactAsync(string name, string? email = null, string? phone = null)
  {
    await page.GetByLabel("Your name").FillAsync(name);
    if (email is not null)
      await page.GetByLabel("Email (optional)").FillAsync(email);
    if (phone is not null)
      await page.GetByLabel("Phone (optional)").FillAsync(phone);
  }

  public async Task FillShippingAddressAsync(string fullName, string line1, string city, string state, string postalCode, string country)
  {
    await ShippingBlock.GetByLabel("Full name").FillAsync(fullName);
    await ShippingBlock.GetByLabel("Address line 1").FillAsync(line1);
    await ShippingBlock.GetByLabel("City").FillAsync(city);
    await ShippingBlock.GetByLabel("State / Province").FillAsync(state);
    await ShippingBlock.GetByLabel("Postal code").FillAsync(postalCode);
    await ShippingBlock.GetByLabel("Country").FillAsync(country);
  }

  // Each MudRadio is a <label> wrapping its input, and the option name is unique to its row.
  public Task ChooseShippingAsync(string optionName) =>
      page.Locator(".mud-radio").Filter(new() { HasText = optionName }).ClickAsync();

  public async Task FillCardAsync(string number, string nameOnCard, string expiry, string cvc)
  {
    await page.GetByLabel("Card number").FillAsync(number);
    await page.GetByLabel("Name on card").FillAsync(nameOnCard);
    await page.GetByLabel("Expiry (MM/YY)").FillAsync(expiry);
    await page.GetByLabel("CVC").FillAsync(cvc);
  }

  public Task PlaceOrderAsync() =>
      page.GetByRole(AriaRole.Button, new() { Name = "Place order" }).ClickAsync();

  public Task WaitForOrderConfirmationAsync(string buyerName, float timeout = 30_000) =>
      ToastHelper.WaitForToastAsync(page, $"Order placed — thanks, {buyerName}!", timeout);

  // "Your order #123 is confirmed ..." - the id the confirmation email's subject carries.
  public async Task<int> GetOrderIdAsync()
  {
    var text = await Confirmation.InnerTextAsync();
    var match = Regex.Match(text, @"#(\d+)");
    return match.Success
        ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
        : throw new InvalidOperationException($"No order number in the confirmation panel: \"{text}\"");
  }
}
