namespace BikeBuilder.MobileApp;

// Dev-loop base addresses. The Android emulator reaches the host machine at 10.0.2.2; the
// Windows head talks to localhost directly. The notifications hub lives on the
// BikeBuilder.Web.Public server (7301), NOT behind the gateway (7500) - the gateway only
// fronts the three APIs.
// Before any store release this needs real configuration: deployed HTTPS gateway/storefront
// URLs (which also lets the cleartext + mixed-content Android allowances go), and a physical
// device needs the host's LAN IP instead of 10.0.2.2.
#pragma warning disable S5332 // Plain http on purpose: these are the local dev endpoints.
public static class AppEnvironment
{
  public static string ApiBaseAddress { get; } =
      OperatingSystem.IsAndroid() ? "http://10.0.2.2:7500" : "http://localhost:7500";

  public static string OrdersApiBaseAddress { get; } = $"{ApiBaseAddress}/orders";

  public static Uri NotificationsHubUri { get; } =
      new(OperatingSystem.IsAndroid()
          ? "http://10.0.2.2:7301/hubs/notifications"
          : "http://localhost:7301/hubs/notifications");
}
#pragma warning restore S5332
