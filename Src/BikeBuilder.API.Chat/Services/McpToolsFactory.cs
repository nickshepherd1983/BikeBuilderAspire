using ModelContextProtocol.Client;

namespace BikeBuilder.API.Chat.Services;

// Connects to the BikeBuilder.MCP server and lists its tools for one chat request. A session
// per request because the caller's bearer token rides along on every MCP call (the orders
// tools are role-gated downstream); the server is stateless, so this is one small round trip.
public sealed class McpToolsFactory(IHttpClientFactory _httpClientFactory, IConfiguration _configuration, ILoggerFactory _loggerFactory)
{
  public const string HttpClientName = "mcp";

  // GrpcChannel-style manual resolution isn't needed for plain HTTP, but the MCP transport
  // wants an absolute endpoint up front, so the service-discovery value is read directly.
#pragma warning disable S1075 // Standalone-run fallback to the MCP server's launch-profile address.
  readonly Uri _endpoint = new(new Uri(
      _configuration["services:mcp:https:0"]
      ?? _configuration["services:mcp:http:0"]
      ?? "https://localhost:7600"), "/mcp");
#pragma warning restore S1075

  public Uri Endpoint => _endpoint;

  public async Task<McpSession> ConnectAsync(string? bearerToken, CancellationToken cancellationToken)
  {
    var options = new HttpClientTransportOptions
    {
      Endpoint = _endpoint,
      Name = "bikebuilder-chat"
    };
    if (bearerToken is not null)
      options.AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearerToken}" };

    var transport = new HttpClientTransport(options, _httpClientFactory.CreateClient(HttpClientName), _loggerFactory);
    var client = await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory, cancellationToken: cancellationToken);
    try
    {
      var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
      return new McpSession(client, tools);
    }
    catch
    {
      await client.DisposeAsync();
      throw;
    }
  }
}

public sealed class McpSession(McpClient _client, IList<McpClientTool> _tools) : IAsyncDisposable
{
  public IList<McpClientTool> Tools => _tools;

  public ValueTask DisposeAsync() => _client.DisposeAsync();
}
