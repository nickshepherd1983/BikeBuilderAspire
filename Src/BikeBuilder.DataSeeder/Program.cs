using BikeBuilder.DataSeeder;

// Seeds the local dev stack with 1000+ real-sounding components, 100 bike builds, and 1-30
// Cosmos ratings per build. Run it as the "dataseeder" resource from the Aspire dashboard
// (the AppHost injects the connection strings), or standalone with the two environment
// variables set by hand.
// Refuses to run against a database that already has components; pass --reset to wipe
// components, bike builds, and ratings first.

var reset = args.Contains("--reset");

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
  Console.WriteLine("The database already contains components - run again with --reset to wipe components, bike builds, and ratings before seeding.");
  return 1;
}

var summary = await DatabaseSeeder.SeedAsync(db, ratingsContainer, new Random(20260827));

Console.WriteLine($"Seeded {summary.Components} components.");
Console.WriteLine($"Seeded {summary.BikeBuilds} bike builds and {summary.Ratings} ratings.");
Console.WriteLine("Ratings were written straight to Cosmos, so no Service Bus notifications were published.");
return 0;
