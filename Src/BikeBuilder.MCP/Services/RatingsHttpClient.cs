namespace BikeBuilder.MCP.Services;

// The ratings Functions app's anonymous read endpoints - the same two the admin app's
// RatingsClient calls.
public class RatingsHttpClient(HttpClient _http)
{
  public async Task<List<RatingDto>> ListAsync(int bikeBuildId, CancellationToken cancellationToken) =>
      await _http.GetFromJsonAsync<List<RatingDto>>($"api/bikebuilds/{bikeBuildId}/ratings", cancellationToken) ?? [];

  // Review text across every build, newest first; the service narrows by phrase and stars.
  public async Task<List<RatingDto>> SearchAsync(string? text, int minStars, int maxStars, int take, CancellationToken cancellationToken)
  {
    var query = $"api/ratings/search?text={Uri.EscapeDataString(text ?? "")}&minStars={minStars}&maxStars={maxStars}&take={take}";
    return await _http.GetFromJsonAsync<List<RatingDto>>(query, cancellationToken) ?? [];
  }

  public async Task<List<RatingSummaryDto>> GetSummariesAsync(IEnumerable<int> bikeBuildIds, CancellationToken cancellationToken)
  {
    var ids = string.Join(',', bikeBuildIds);
    if (ids.Length == 0)
      return [];

    return await _http.GetFromJsonAsync<List<RatingSummaryDto>>($"api/bikebuilds/ratings/summaries?ids={ids}", cancellationToken) ?? [];
  }
}

public sealed record RatingDto(string Id, string BikeBuildId, int Stars, string? Comment, string UserName, DateTimeOffset CreatedAt);

// BikeBuildId is a string on the wire (it is the Cosmos partition key); tools convert it back.
public sealed record RatingSummaryDto(string BikeBuildId, int Count, double AverageStars);
