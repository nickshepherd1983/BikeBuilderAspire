using System.Net;
using System.Text;

namespace BikeBuilder.Test.Integration;

// The W3C trace id is the system's correlation id: a caller that sends a traceparent gets the
// same trace id back in X-Trace-Id from whichever service answered. Going through the gateway
// proves the propagation hop (YARP or the APIM self-hosted gateway) and the services' response
// middleware in one deterministic request.
[Collection("BikeBuilderApp")]
public class TraceHeaderTests(BikeBuilderAppFixture fixture)
{
  [Fact]
  public async Task Responses_echo_the_callers_trace_id()
  {
    const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    using var http = new HttpClient();
    using var request = new HttpRequestMessage(HttpMethod.Post, $"{fixture.GatewayBaseAddress}/orders/graphql")
    {
      Content = new StringContent("""{"query":"{ shippingOptions { method } }"}""", Encoding.UTF8, "application/json")
    };
    request.Headers.TryAddWithoutValidation("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");

    using var response = await http.SendAsync(request);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(traceId, Assert.Single(response.Headers.GetValues("X-Trace-Id")));
  }
}
