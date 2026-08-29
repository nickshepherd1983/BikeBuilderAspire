# BikeBuilder

> **Heads up: this is prototype-grade code.** This project is my first time using Claude-based
> development, and I'm using it to noodle on different approaches and see what sticks. Treat it
> as a proof of concept / playground rather than a reference implementation — corners are cut,
> patterns shift between features as I experiment, and nothing here is production-hardened.

BikeBuilder is a small microservices playground for building custom bikes: manage a catalog of
components (with images), assemble them into bike builds, rate the builds, and watch activity
land in real time on a public site.

## What's in the solution

| Project | What it is |
| --- | --- |
| `BikeBuilder.AppHost` | .NET Aspire app host — the one thing you run: orchestrates SQL Server, Azurite, the Service Bus and Cosmos emulators, and all four apps |
| `BikeBuilder.ServiceDefaults` | Shared Aspire service defaults: OpenTelemetry (traces, metrics, logs), health checks, service discovery |
| `BikeBuilder.Web` | Blazor WebAssembly front end (MudBlazor), Auth0 login, talks gRPC-Web to the API and REST to the Ratings service |
| `BikeBuilder.API` | ASP.NET Core gRPC API (EF Core + SQL Server), component image upload to Azure Blob Storage, publishes events to Service Bus |
| `BikeBuilder.API.Ratings` | Azure Functions (.NET isolated) ratings microservice backed by Cosmos DB, JWT-secured via Auth0 |
| `BikeBuilder.Web.Public` | Blazor Server public site showing live activity toasts (Service Bus → SignalR) |
| `BikeBuilder.Contracts` | Shared event/message contracts |
| `BikeBuilder.DataSeeder` | Console tool that fills the local dev stack with 1000+ real-sounding components, 100 bike builds, and 1–30 ratings each |
| `BikeBuilder.Test.Integration` | End-to-end smoke test: the Aspire testing host boots the whole system (with a stub OIDC issuer standing in for Auth0) and Playwright drives the real UI, recording video |

## Running it

Prerequisites: Docker Desktop, the .NET 10 SDK, and
[Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
≥ 4.0.6280 (Aspire launches the Functions app through `func start`).

```powershell
dotnet run --project Src/BikeBuilder.AppHost
```

(or F5 on the AppHost project in Visual Studio, or `aspire run`). The Aspire dashboard opens
automatically: every backing service and app with its endpoints, logs, and telemetry in one
place. The web app is at https://localhost:7200, the public site at https://localhost:7300.

The emulator containers are persistent and keep their data across AppHost runs (SQL, blobs,
and Cosmos documents survive a restart). Auth is a real Auth0 tenant in local dev; integration
tests swap in a stub OIDC issuer so they run fully offline.

To fill the dev stack with realistic sample data (1000+ components, 100 bike builds, ratings),
start the `dataseeder` resource from the Aspire dashboard (it's marked explicit-start, so it
only runs when you tell it to). Running it a second time refuses to touch a non-empty database;
to wipe and reseed, run it by hand with the connection strings from the dashboard's environment
view and pass `--reset`.

## Telemetry

Every server app (API, Web.Public, Ratings) exports OpenTelemetry traces, metrics, and logs
over OTLP to the Aspire dashboard — open the Traces view to follow a single request across
API → SQL/Blob → Service Bus → Web.Public → SignalR broadcast (the Service Bus consumer may
appear as a linked trace reference rather than a nested span — that's the messaging
convention, click through it). Telemetry is in-memory and resets with the AppHost.

## Tests

```powershell
dotnet test Src/BikeBuilder.Test.Integration
```

One end-to-end test covers the whole journey: log in, create a component, upload an image,
build a bike, rate it, and verify the live toasts on the public site. Requires Docker and the
Azure Functions Core Tools. The Aspire testing host (`Aspire.Hosting.Testing`) runs the same
AppHost in test mode: fixed 18xxx ports, a stub OIDC issuer instead of Auth0, and
session-scoped containers that are torn down with the fixture. Debugging the test from Visual
Studio's Test Explorer runs the browser headed; videos land in `TestResults/videos`.
