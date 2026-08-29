using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using BikeBuilder.API.Data;
using BikeBuilder.DataSeeder;
using Microsoft.EntityFrameworkCore;

namespace BikeBuilder.Test.Integration;

// Boots the whole system through the Aspire AppHost in test mode (IntegrationTest=true):
// SQL Server, Azurite, the Service Bus and Cosmos emulators, a stub OIDC issuer standing in
// for Auth0, and all four apps - then seeds realistic data and hands Playwright a browser.
public sealed class BikeBuilderAppFixture : IAsyncLifetime
{
  public const string OidcTestUsername = "testuser";
  public const string OidcTestPassword = "password";

  // Fixed ports, defined in the AppHost's test branch and baked into the WASM app's
  // wwwroot/appsettings.IntegrationTest.json - the browser can't read dynamic Aspire
  // endpoints, so the whole test topology agrees on these up front.
  //
  // 127.0.0.1 rather than "localhost" - on this Windows/Docker Desktop setup, the .NET
  // HttpClient and the Chromium browser Playwright launches were observed resolving
  // "localhost" differently, with only one of the two reliably connecting.
  public string ApiBaseAddress => "http://127.0.0.1:18100";
  public string WebBaseAddress => "http://127.0.0.1:18200";
  public string WebPublicBaseAddress => "http://127.0.0.1:18300";
  public string RatingsBaseAddress => "http://127.0.0.1:18500";
  public string OrdersBaseAddress => "http://127.0.0.1:18600";

  public IBrowser Browser { get; private set; } = null!;

  public static readonly string VideosDir = Path.Combine(AppContext.BaseDirectory, "TestResults", "videos");

  DistributedApplication _app = null!;
  IPlaywright _playwright = null!;

  public async Task InitializeAsync()
  {
    var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
    if (exitCode != 0)
    {
      throw new InvalidOperationException($"playwright install failed with exit code {exitCode}");
    }

    // RandomizePorts=false: the testing builder randomizes endpoint ports by default, but
    // the WASM app's baked-in config (and the OIDC issuer the tokens are minted for)
    // require the fixed 18xxx ports the AppHost's test branch pins.
    var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.BikeBuilder_AppHost>(
        ["IntegrationTest=true", "DcpPublisher:RandomizePorts=false"]);
    _app = await builder.BuildAsync();

    // 20 minutes: a cold CI runner pulls every emulator image (SQL Server 2025, Azurite,
    // Service Bus + its SQL Edge companion, Cosmos vNext) before anything can start; 10
    // proved too tight. Local runs with cached images start in a fraction of this.
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
    await _app.StartAsync(cts.Token);

    // api healthy => SQL up, blob container and Service Bus queue provisioned. ratings
    // healthy => Functions host + worker up and the Cosmos database/container provisioned
    // (its probe hits the anonymous warmup endpoint, which reads Cosmos).
    var notifications = _app.ResourceNotifications;
    try
    {
      await Task.WhenAll(
          notifications.WaitForResourceHealthyAsync("oidc-mock", cts.Token),
          notifications.WaitForResourceHealthyAsync("api", cts.Token),
          notifications.WaitForResourceHealthyAsync("orders", cts.Token),
          notifications.WaitForResourceHealthyAsync("ratings", cts.Token),
          notifications.WaitForResourceHealthyAsync("web", cts.Token),
          notifications.WaitForResourceHealthyAsync("web-public", cts.Token));
    }
    catch
    {
      // Startup failures on CI are otherwise undiagnosable - the teardown deletes the
      // orchestrator's session logs before the workflow can dump them, so capture each
      // resource's output into TestResults (which CI uploads) while it still exists.
      await DumpResourceLogsAsync();
      throw;
    }

    // Seed the same realistic dataset local dev uses (1000+ components, 100 builds,
    // 1-30 ratings each) so the tests exercise pagination, search, and summaries
    // against real volumes rather than an empty database.
    var sqlConnectionString = await _app.GetConnectionStringAsync("BikeBuilderDb", cts.Token)
        ?? throw new InvalidOperationException("No connection string for BikeBuilderDb.");
    var cosmosConnectionString = await _app.GetConnectionStringAsync("cosmos", cts.Token)
        ?? throw new InvalidOperationException("No connection string for cosmos.");

    var options = new DbContextOptionsBuilder<BikeBuilderDbContext>()
        .UseSqlServer(sqlConnectionString)
        .Options;
    await using (var db = new BikeBuilderDbContext(options))
    {
      // The API (running as Development) already migrates at startup; this is an idempotent
      // safety net so seeding never races an unmigrated schema.
      await db.Database.MigrateAsync();

      using var cosmosClient = DatabaseSeeder.CreateEmulatorCosmosClient(cosmosConnectionString);

      Microsoft.Azure.Cosmos.Container? ratingsContainer = null;
      // The container is provisioned by the AppHost, but the emulator's data plane can lag
      // its readiness signal - retry briefly rather than race it.
      await WaitUntilSucceedsAsync(
          async () => ratingsContainer = await DatabaseSeeder.EnsureRatingsContainerAsync(cosmosClient),
          timeoutSeconds: 120);

      await DatabaseSeeder.SeedAsync(db, ratingsContainer!, new Random(20260827));
    }

    _playwright = await Playwright.CreateAsync();
    // Set HEADED=1 to watch the browser while the test runs (e.g. `$env:HEADED=1` in
    // PowerShell before `dotnet test`, or via .runsettings). Debugging the test (Visual
    // Studio Test Explorer "Debug") attaches a debugger, so that runs headed too.
    var headed = Environment.GetEnvironmentVariable("HEADED") == "1" || Debugger.IsAttached;
    Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
      Headless = !headed,
      SlowMo = headed ? 250 : 0,
      // Chrome's speculative background-networking features (preconnect, DNS/network
      // prediction, connection warm-up heuristics) have been observed interacting badly
      // with this environment's proxied loopback ports, surfacing as intermittent
      // ERR_CONNECTION_REFUSED on the app's own gRPC-Web calls despite the same origin
      // being reachable via a plain fetch moments before or after - disable that class of
      // feature outright rather than chase it further.
      Args =
        [
            "--disable-background-networking",
                "--disable-features=NetworkPrediction,PreconnectToSearch",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-renderer-backgrounding",
                "--disable-ipc-flooding-protection",
                "--no-first-run",
            ],
    });
  }

  /// <summary>
  /// Creates a page in a context that records video to TestResults/videos. Playwright only
  /// finalizes the video when the context closes, so callers must dispose the page via
  /// <see cref="SaveVideoAsync"/> (which closes the context) rather than page.CloseAsync().
  /// </summary>
  public async Task<IPage> CreatePageAsync()
  {
    var context = await Browser.NewContextAsync(new()
    {
      RecordVideoDir = VideosDir,
      RecordVideoSize = new() { Width = 1280, Height = 720 }
    });
    // CI runners are heavily oversubscribed (six emulator containers + five apps + three
    // recording browser pages on four cores), so give actions and waits far more headroom
    // than Playwright's 30s default. Locally everything completes as fast as ever - waits
    // return the moment their condition is met.
    context.SetDefaultTimeout(60_000);
    return await context.NewPageAsync();
  }

  /// <summary>Closes the page's context (finalizing the recording) and renames the video.</summary>
  public static async Task SaveVideoAsync(IPage page, string name)
  {
    await page.Context.CloseAsync();
    if (page.Video is not null)
    {
      await page.Video.SaveAsAsync(Path.Combine(VideosDir, $"{name}.webm"));
      await page.Video.DeleteAsync();
    }
  }

  async Task DumpResourceLogsAsync()
  {
    var resultsDir = Path.Combine(AppContext.BaseDirectory, "TestResults");
    Directory.CreateDirectory(resultsDir);
    var loggerService = _app.Services.GetRequiredService<ResourceLoggerService>();

    foreach (var resourceName in new[] { "oidc-mock", "api", "orders", "ratings", "web", "web-public" })
    {
      var lines = new List<string>();
      try
      {
        // WatchAsync streams until the resource completes, so bound the replay of the
        // backlog with a short timeout and keep whatever arrived.
        using var watchCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var batch in loggerService.WatchAsync(resourceName).WithCancellation(watchCts.Token))
        {
          foreach (var line in batch)
            lines.Add(line.Content);
        }
      }
      catch (OperationCanceledException)
      {
        // Expected - the stream stays open while the resource lives.
      }
      catch (Exception ex)
      {
        lines.Add($"(failed to capture logs: {ex.Message})");
      }

      await File.WriteAllLinesAsync(Path.Combine(resultsDir, $"startup-{resourceName}.log"), lines);
    }
  }

  static async Task WaitUntilSucceedsAsync(Func<Task> action, int timeoutSeconds = 90)
  {
    var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
    while (true)
    {
      try
      {
        await action();
        return;
      }
      catch (Exception ex)
      {
        if (DateTime.UtcNow >= deadline)
          throw new InvalidOperationException($"Action did not succeed within {timeoutSeconds}s.", ex);

        await Task.Delay(TimeSpan.FromSeconds(5));
      }
    }
  }

  public async Task DisposeAsync()
  {
    // InitializeAsync may have thrown partway through, leaving later fields unset - guard
    // each teardown step so a partial-startup failure doesn't also mask a NullReferenceException.
    if (Browser is not null)
      await Browser.CloseAsync();

    _playwright?.Dispose();

    // Tears down DCP and every container/app the AppHost started.
    if (_app is not null)
      await _app.DisposeAsync();
  }
}
