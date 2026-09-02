namespace BikeBuilder.MCP.Tools;

// Rating tools over the ratings Functions app's anonymous reads, joined to the catalog for
// build names and prices where a ranked answer needs them.
[McpServerToolType]
public sealed class RatingsTools(RatingsHttpClient _ratings, BikeBuildService.BikeBuildServiceClient _bikeBuilds)
{
  // Ids travel in a query string, so summaries are fetched in batches; builds are paged at
  // the API's maximum and the walk is bounded so a runaway catalog can't stall a tool call.
  const int SummaryBatchSize = 50;
  const int MaxBuildPages = 40;

  [McpServerTool(Name = "list_ratings", ReadOnly = true, Idempotent = true),
   Description("Lists the star ratings and comments customers left for one bike build, newest first, with the count and average.")]
  public async Task<BikeBuildRatings> ListRatings(
      [Description("The bike build id.")] int bikeBuildId,
      CancellationToken cancellationToken = default)
  {
    var ratings = await _ratings.ListAsync(bikeBuildId, cancellationToken);
    return new BikeBuildRatings(
        bikeBuildId,
        ratings.Count,
        ratings.Count == 0 ? 0 : Math.Round(ratings.Average(rating => rating.Stars), 2),
        [.. ratings.Select(rating => new Rating(rating.Stars, rating.Comment, rating.UserName, ToolSupport.Date(rating.CreatedAt)))]);
  }

  [McpServerTool(Name = "get_rating_summaries", ReadOnly = true, Idempotent = true),
   Description("Gets the rating count and average stars for several bike builds at once. Builds with no ratings are omitted.")]
  public async Task<List<RatingSummary>> GetRatingSummaries(
      [Description("The bike build ids to summarise.")] int[] bikeBuildIds,
      CancellationToken cancellationToken = default)
  {
    var summaries = new List<RatingSummary>();
    foreach (var batch in bikeBuildIds.Distinct().Chunk(SummaryBatchSize))
      summaries.AddRange((await _ratings.GetSummariesAsync(batch, cancellationToken)).Select(ToSummary));

    return summaries;
  }

  [McpServerTool(Name = "top_rated_bike_builds", ReadOnly = true, Idempotent = true),
   Description("Ranks bike builds by average star rating (ties broken by rating count), returning name, total price, rating count and average. Set lowestFirst to find the worst rated builds. Only builds with at least minRatings ratings are included.")]
  public async Task<List<RatedBikeBuild>> TopRatedBikeBuilds(
      [Description("Minimum number of ratings a build needs to be ranked. Default 3.")] int minRatings = 3,
      [Description("How many builds to return, 1 to 50. Default 10.")] int take = 10,
      [Description("True to rank from the lowest average upwards.")] bool lowestFirst = false,
      CancellationToken cancellationToken = default)
  {
    var builds = await ListAllBikeBuildsAsync(cancellationToken);

    var summaries = new List<RatingSummary>();
    foreach (var batch in builds.Select(build => build.Id).Chunk(SummaryBatchSize))
      summaries.AddRange((await _ratings.GetSummariesAsync(batch, cancellationToken)).Select(ToSummary));

    var ranked = builds
        .Join(summaries.Where(summary => summary.Count >= Math.Max(1, minRatings)),
            build => build.Id, summary => summary.BikeBuildId,
            (build, summary) => new RatedBikeBuild(build.Id, build.Name, build.Total, summary.Count, summary.AverageStars));

    ranked = lowestFirst
        ? ranked.OrderBy(build => build.AverageStars).ThenByDescending(build => build.RatingCount)
        : ranked.OrderByDescending(build => build.AverageStars).ThenByDescending(build => build.RatingCount);

    return [.. ranked.Take(ToolSupport.PageSize(take))];
  }

  async Task<List<BikeBuildSummary>> ListAllBikeBuildsAsync(CancellationToken cancellationToken)
  {
    var builds = new List<BikeBuildSummary>();
    for (var page = 1; page <= MaxBuildPages; page++)
    {
      var response = await _bikeBuilds.ListBikeBuildsAsync(
          new ListBikeBuildsRequest { Page = page, PageSize = ToolSupport.MaxPageSize },
          cancellationToken: cancellationToken);
      builds.AddRange(response.BikeBuilds.Select(CatalogTools.ToSummary));
      if (response.BikeBuilds.Count == 0 || builds.Count >= response.TotalCount)
        break;
    }

    return builds;
  }

  static RatingSummary ToSummary(RatingSummaryDto summary) => new(
      int.TryParse(summary.BikeBuildId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0,
      summary.Count,
      Math.Round(summary.AverageStars, 2));
}

// CreatedAt and Total are pre-formatted strings (MM/dd/yyyy HH:mm UTC and $1,234.56) - see ToolSupport.
public sealed record Rating(int Stars, string? Comment, string UserName, string CreatedAt);

public sealed record BikeBuildRatings(int BikeBuildId, int Count, double AverageStars, IReadOnlyList<Rating> Ratings);

public sealed record RatingSummary(int BikeBuildId, int Count, double AverageStars);

public sealed record RatedBikeBuild(int Id, string Name, string Total, int RatingCount, double AverageStars);
