namespace BikeBuilder.Test.Integration.PageObjects;

// MudBlazor snackbars render with role=alert in both front ends, so one wait works for the
// WASM app and the Web.Public storefront alike.
static class ToastHelper
{
  public static Task WaitForToastAsync(IPage page, string expectedTextSubstring, float timeout = 30_000) =>
      Expect(page.GetByRole(AriaRole.Alert).Filter(new() { HasText = expectedTextSubstring }))
          .ToBeVisibleAsync(new() { Timeout = timeout });
}
