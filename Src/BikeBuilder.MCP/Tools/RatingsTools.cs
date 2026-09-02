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
   Description("Reads the reviews customers left for one bike build, newest first: each star rating with the review text (comment) and reviewer, plus the count and average over the whole build. Use this to report what reviewers say about a specific build; narrow by stars to see only praise or only complaints.")]
  public async Task<BikeBuildRatings> ListRatings(
      [Description("The bike build id.")] int bikeBuildId,
      [Description("Lowest star rating to include, 1-5. Default 1.")] int minStars = 1,
      [Description("Highest star rating to include, 1-5. Default 5.")] int maxStars = 5,
      CancellationToken cancellationToken = default)
  {
    var ratings = await _ratings.ListAsync(bikeBuildId, cancellationToken);
    var (low, high) = StarRange(minStars, maxStars);
    return new BikeBuildRatings(
        bikeBuildId,
        ratings.Count,
        ratings.Count == 0 ? 0 : Math.Round(ratings.Average(rating => rating.Stars), 2),
        [.. ratings
            .Where(rating => rating.Stars >= low && rating.Stars <= high)
            .Select(rating => new Rating(rating.Stars, rating.Comment, rating.UserName, ToolSupport.Date(rating.CreatedAt)))]);
  }

  [McpServerTool(Name = "search_rating_comments", ReadOnly = true, Idempotent = true),
   Description("Reads what customers wrote in their reviews across every bike build, newest first, each with the build it is about. Optionally only comments containing a word or phrase, and/or within a star range. Use this for questions about what reviewers say, praise or complain about in general, or to find reviews mentioning something (for example brakes, weight, comfort).")]
  public async Task<List<RatingComment>> SearchRatingComments(
      [Description("Word or phrase the review text must contain (case-insensitive). Omit for the newest reviews overall.")] string? text = null,
      [Description("Lowest star rating to include, 1-5. Default 1.")] int minStars = 1,
      [Description("Highest star rating to include, 1-5. Default 5.")] int maxStars = 5,
      [Description("How many reviews to return, 1 to 50. Default 20.")] int take = 20,
      CancellationToken cancellationToken = default)
  {
    var (low, high) = StarRange(minStars, maxStars);
    var ratings = await _ratings.SearchAsync(text, low, high, ToolSupport.PageSize(take), cancellationToken);
    if (ratings.Count == 0)
      return [];

    // Ratings only carry the build id; the names come from the catalog so the answer can say
    // which bike each review is about.
    var names = (await ListAllBikeBuildsAsync(cancellationToken)).ToDictionary(build => build.Id, build => build.Name);
    return [.. ratings.Select(rating =>
    {
      var id = ParseId(rating.BikeBuildId);
      return new RatingComment(id, names.GetValueOrDefault(id, "(unknown build)"), rating.Stars, rating.Comment ?? "", rating.UserName, ToolSupport.Date(rating.CreatedAt));
    })];
  }

  static (int Low, int High) StarRange(int minStars, int maxStars)
  {
    var low = Math.Clamp(minStars, 1, 5);
    var high = Math.Clamp(maxStars, 1, 5);
    return low <= high ? (low, high) : (high, low);
  }

  static int ParseId(string bikeBuildId) =>
      int.TryParse(bikeBuildId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

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
      ParseId(summary.BikeBuildId),
      summary.Count,
      Math.Round(summary.AverageStars, 2));
}

// CreatedAt and Total are pre-formatted strings (MM/dd/yyyy HH:mm UTC and $1,234.56) - see ToolSupport.
public sealed record Rating(int Stars, string? Comment, string UserName, string CreatedAt);

public sealed record RatingComment(int BikeBuildId, string BikeBuildName, int Stars, string Comment, string UserName, string CreatedAt);

public sealed record BikeBuildRatings(int BikeBuildId, int Count, double AverageStars, IReadOnlyList<Rating> Ratings);

public sealed record RatingSummary(int BikeBuildId, int Count, double AverageStars);

public sealed record RatedBikeBuild(int Id, string Name, string Total, int RatingCount, double AverageStars);
