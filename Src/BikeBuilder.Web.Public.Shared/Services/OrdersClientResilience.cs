using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace BikeBuilder.Web.Public.Services;

/// <summary>
/// Resilience handler for the generated orders GraphQL client, applied identically by the
/// server half, the WASM half, and the MAUI app.
/// </summary>
/// <remarks>
/// GraphQL is always a POST, so the default handler (which no longer retries unsafe methods)
/// would give the cart mutations nothing. The retry here is narrowed to cases where the request
/// provably never reached the resolvers - a connection failure, or a 503 from the gateway - and
/// deliberately excludes attempt timeouts and 500s: an <c>addOrderItem</c> that executed but lost
/// its response would bump the quantity twice, and a replayed <c>processOrder</c> would find the
/// cart already claimed. Timeouts and the circuit breaker keep the standard defaults.
/// </remarks>
public static class OrdersClientResilience
{
  // EXTEXP0001: RemoveAllResilienceHandlers is still marked experimental, but it's the
  // documented way to swap a ConfigureHttpClientDefaults handler for a per-client one.
#pragma warning disable EXTEXP0001
  public static void Configure(IHttpClientBuilder builder) =>
      builder
          // The server half already has the shared default from ServiceDefaults; replace rather
          // than stack. No-op in the WASM/MAUI heads, which have no default handler.
          .RemoveAllResilienceHandlers()
          .AddStandardResilienceHandler(options =>
              options.Retry.ShouldHandle = args => ValueTask.FromResult(args.Outcome switch
              {
                { Exception: HttpRequestException } => true,
                { Result.StatusCode: HttpStatusCode.ServiceUnavailable } => true,
                _ => false
              }));
#pragma warning restore EXTEXP0001
}
