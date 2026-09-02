using BikeBuilder.DataSeeder;

// Seeds the local dev stack with 1000+ real-sounding components, 100 bike builds, and 1-30
// Cosmos ratings per build. Run it as the "dataseeder" resource from the Aspire dashboard
// (the AppHost injects the connection strings), or standalone with the two environment
// variables set by hand.
// Refuses to run against a database that already has components; pass --reset to wipe
// components, bike builds, and ratings first, or --ratings-only to leave the catalog alone
// and seed ratings for the bike builds it already has (refused if any ratings exist).

var reset = args.Contains("--reset");
var ratingsOnly = args.Contains("--ratings-only");

var sqlConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BikeBuilderDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings__BikeBuilderDb is not set. Start the seeder from the Aspire dashboard (dataseeder resource), or set the environment variable.");
var cosmosConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__cosmos")
    ?? throw new InvalidOperationException(
        "ConnectionStrings__cosmos is not set. Start the seeder from the Aspire dashboard (dataseeder resource), or set the environment variable.");

var dbOptions = new DbContextOptionsBuilder<BikeBuilderDbContext>().UseSqlServer(sqlConnectionString).Options;
await using var db = new BikeBuilderDbContext(dbOptions);
await db.Database.MigrateAsync();

using var cosmos = DatabaseSeeder.CreateEmulatorCosmosClient(cosmosConnectionString);
var ratingsContainer = await DatabaseSeeder.EnsureRatingsContainerAsync(cosmos);

if (ratingsOnly)
{
  var existing = await DatabaseSeeder.CountRatingsAsync(ratingsContainer);
  if (existing > 0)
  {
    Console.WriteLine($"The ratings container already holds {existing} ratings - --ratings-only seeds only an empty one (use --reset to start over).");
    return 1;
  }

  var buildIds = await db.BikeBuilds.Select(build => build.Id).ToListAsync();
  if (buildIds.Count == 0)
  {
    Console.WriteLine("There are no bike builds to rate - run without --ratings-only to seed the catalog first.");
    return 1;
  }

  var ratingCount = await DatabaseSeeder.SeedRatingsAsync(buildIds, ratingsContainer, new Random(20260827));
  Console.WriteLine($"Seeded {ratingCount} ratings across {buildIds.Count} existing bike builds.");
  Console.WriteLine("Ratings were written straight to Cosmos, so no Service Bus notifications were published.");
  return 0;
}

if (reset)
{
  Console.WriteLine("--reset: deleting existing bike builds, components, and ratings...");
  await db.BikeBuildComponents.ExecuteDeleteAsync();
  await db.BikeBuilds.ExecuteDeleteAsync();
  await db.ComponentImages.ExecuteDeleteAsync();
  await db.Components.ExecuteDeleteAsync();
  await ratingsContainer.DeleteContainerAsync();
  ratingsContainer = await DatabaseSeeder.EnsureRatingsContainerAsync(cosmos);
}
else if (await db.Components.AnyAsync())
{
  Console.WriteLine("The database already contains components - run again with --reset to wipe components, bike builds, and ratings before seeding, or --ratings-only to add ratings to the existing builds.");
  return 1;
}

var summary = await DatabaseSeeder.SeedAsync(db, ratingsContainer, new Random(20260827));

Console.WriteLine($"Seeded {summary.Components} components.");
Console.WriteLine($"Seeded {summary.BikeBuilds} bike builds and {summary.Ratings} ratings.");
Console.WriteLine("Ratings were written straight to Cosmos, so no Service Bus notifications were published.");
return 0;
