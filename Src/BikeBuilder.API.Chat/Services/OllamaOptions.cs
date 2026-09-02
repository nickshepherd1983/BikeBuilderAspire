using System.Data.Common;

namespace BikeBuilder.API.Chat.Services;

// Where the model runs and which one. The AppHost passes the endpoint and model as the
// "ollama" connection string (Endpoint=http://localhost:11434;Model=qwen3.5) so they show up
// as a resource in the dashboard and can be overridden per machine with user secrets; the
// Ollama:* keys tune the completions.
public sealed class OllamaOptions
{
  public const string HttpClientName = "ollama";
  public const string ConnectionName = "ollama";
  public const string DefaultModel = "qwen3.5";
#pragma warning disable S1075 // The Ollama install default; overridden by the connection string.
  public const string DefaultEndpoint = "http://localhost:11434";
#pragma warning restore S1075

  public Uri Endpoint { get; init; } = new(DefaultEndpoint);
  public string Model { get; init; } = DefaultModel;
  // Reasoning ("thinking") models deliberate before every tool call and answer; off by default
  // because it multiplies latency on a local GPU for little gain on lookup questions.
  public bool Think { get; init; }
  public float Temperature { get; init; } = 0.2f;

  public static OllamaOptions FromConfiguration(IConfiguration configuration)
  {
    var endpoint = DefaultEndpoint;
    var model = DefaultModel;

    var connectionString = configuration.GetConnectionString(ConnectionName);
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
      var parts = new DbConnectionStringBuilder { ConnectionString = connectionString };
      if (parts.TryGetValue("Endpoint", out var configuredEndpoint) && configuredEndpoint is string { Length: > 0 } endpointValue)
        endpoint = endpointValue;
      if (parts.TryGetValue("Model", out var configuredModel) && configuredModel is string { Length: > 0 } modelValue)
        model = modelValue;
    }

    return new OllamaOptions
    {
      Endpoint = new Uri(endpoint),
      Model = model,
      Think = configuration.GetValue("Ollama:Think", false),
      Temperature = configuration.GetValue("Ollama:Temperature", 0.2f)
    };
  }

  // Ollama lists "qwen3.5:latest" for a model pulled as "qwen3.5".
  public bool Matches(string installedModelName) =>
      string.Equals(installedModelName, Model, StringComparison.OrdinalIgnoreCase)
      || string.Equals(installedModelName, Model + ":latest", StringComparison.OrdinalIgnoreCase);
}
