using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace BikeBuilder.API.Notifications.Functions;

public static class HealthFunctions
{
  // The AppHost's health probe. The Functions host's own "/" answers 200 long before the
  // worker process is up, so a passing probe here is what proves the worker (and therefore
  // the Service Bus trigger) is actually running - same reasoning as the ratings warmup probe.
  [Function("Health")]
  public static HttpResponseData Health(
      [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData request) =>
      request.CreateResponse(HttpStatusCode.OK);
}
