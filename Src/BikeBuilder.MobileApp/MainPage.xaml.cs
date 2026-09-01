namespace BikeBuilder.MobileApp;

public partial class MainPage : ContentPage
{
  public MainPage()
  {
    InitializeComponent();

    blazorWebView.BlazorWebViewInitialized += (_, e) =>
    {
#if ANDROID
      // The WebView serves the app from an https pseudo-origin, so <img> requests to the
      // plain-http dev gateway are "mixed content" and silently blocked by default. Only
      // the WebView's own fetches are affected - .NET's gRPC/GraphQL/SignalR calls bypass
      // the WebView entirely. Remove along with cleartext HTTP when the endpoints move to
      // HTTPS for a store release.
      e.WebView.Settings.MixedContentMode = Android.Webkit.MixedContentHandling.AlwaysAllow;
#endif
    };
  }
}
