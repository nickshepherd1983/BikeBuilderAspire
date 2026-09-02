using Grpc.Core;
using Grpc.Net.Client.Configuration;

namespace BikeBuilder.Contracts.Grpc;

/// <summary>
/// gRPC-level retry policy for the catalog API's read methods, shared by every client head
/// (Orders, Web.Public server half, Web.Admin, Web.Public.Client, MobileApp).
/// </summary>
/// <remarks>
/// The HTTP resilience handler can't help here: a failed gRPC call arrives as HTTP 200 with a
/// grpc-status trailer, and gRPC-Web calls are all POSTs, which the handler no longer retries.
/// Grpc.Net.Client surfaces connection failures (refused, reset, DNS) as Unavailable too, so this
/// one status covers both a briefly-down API and a cold-starting one. Writes are deliberately
/// left out - Create/Update/Delete/Add/Remove aren't idempotent.
/// </remarks>
public static class CatalogGrpcRetry
{
  static readonly (string Service, string Method)[] ReadMethods =
  [
    ("bikebuilder.ComponentService", "ListComponents"),
    ("bikebuilder.ComponentService", "GetComponent"),
    ("bikebuilder.BikeBuildService", "ListBikeBuilds"),
    ("bikebuilder.BikeBuildService", "GetBikeBuild"),
    ("bikebuilder.BikeBuildService", "ListBikeBuildComponents"),
  ];

  public static ServiceConfig CreateServiceConfig()
  {
    var config = new ServiceConfig();
    foreach (var (service, method) in ReadMethods)
    {
      config.MethodConfigs.Add(new MethodConfig
      {
        Names = { new MethodName { Service = service, Method = method } },
        RetryPolicy = new RetryPolicy
        {
          MaxAttempts = 4,
          InitialBackoff = TimeSpan.FromMilliseconds(500),
          MaxBackoff = TimeSpan.FromSeconds(5),
          BackoffMultiplier = 2,
          RetryableStatusCodes = { StatusCode.Unavailable }
        }
      });
    }

    return config;
  }
}
