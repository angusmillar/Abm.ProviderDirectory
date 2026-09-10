# Core.Api end-to-end integration tests + health endpoints

Date: 2026-09-10
Status: Approved for planning

## Purpose

Add a new integration test project, `Abm.PD.Core.Api.Tests`, that exercises `Abm.PD.Core.Api`
end-to-end against a real, disposable PostgreSQL container: real HTTP semantics, real routing/model
binding/serialisation, real EF Core queries against real Postgres — with the API itself hosted
in-process via `WebApplicationFactory<Program>` rather than as a separate Docker container.

This mirrors the integration-test shape already proven in the sibling `PyroServer` solution
(`Abm.Pyro.Api.Test`: `Testcontainers.MsSql` + `WebApplicationFactory<Program>` + `Respawn`) —
same pattern, Postgres instead of SQL Server. See "Prior art" below for the specific things carried
across.

Alongside this, add production `/health/live` and `/health/ready` endpoints to `Abm.PD.Core.Api`
itself, and a `Dockerfile` for real deployment. Neither is exercised by the test project — the API
under test is hosted in-process, not from the built image — but both are useful production artifacts
delivered together with this work since they touch the same project.

## Prior art: `Abm.Pyro.Api.Test`

The design below was arrived at by first drafting a container-based approach (API + Postgres both as
Docker containers, driven over real HTTP), then reconsidering: debugging a failing test would require
attaching a remote debugger to a separate container process, and every run would pay a Docker-image-build
cost. Inspecting `PyroServer`'s existing integration tests confirmed a better-established alternative
already in use across this codebase's sibling solution:

- `Testcontainers.MsSql` (→ `Testcontainers.PostgreSql` for us) for the database only — no container
  for the API.
- `WebApplicationFactory<Program>` hosts the API in-process, so breakpoints work normally and there's
  no image-build step in the test loop. Requires `public partial class Program { }` at the end of
  `Program.cs` — top-level statements otherwise generate an `internal` `Program` class the test
  assembly can't reference.
- A custom `XxxWebApplicationFactory : WebApplicationFactory<Program>` overrides `ConfigureWebHost` to
  inject the container's connection string via `ConfigureAppConfiguration(...AddInMemoryCollection(...))`.
- Migrations are applied **programmatically in the fixture**, once, straight after the container
  starts (`await context.Database.MigrateAsync()`) — not via the app's own startup path.
- **Respawn** resets the database to a clean checkpoint before every single test (not just once per
  run), via an abstract base test class whose `IAsyncLifetime.InitializeAsync` calls
  `Fixture.ResetDatabaseAsync()`. xunit creates a fresh test-class instance per `[Fact]`, so this runs
  before every test.
- A `Dockerfile` exists for the API (`Abm.Pyro.Api/Dockerfile`), entirely unrelated to and unused by
  the tests — for real deployment only.

## Scope

In scope:
- A new `Dockerfile` for `Abm.PD.Core.Api` (none exists today) for real deployment, matching
  `Abm.Pyro.Api/Dockerfile`'s structure and base images.
- `/health/live` and `/health/ready` endpoints on `Abm.PD.Core.Api`, using
  `Microsoft.Extensions.Diagnostics.HealthChecks` + `AspNetCore.HealthChecks.NpgSql`.
- Adding `public partial class Program { }` to `Abm.PD.Core.Api/Program.cs`.
- A new `Abm.PD.Core.Api.Tests` xunit project: `Testcontainers.PostgreSql` for the database,
  `WebApplicationFactory<Program>` for the API, `Respawn` for per-test reset — exercising the full
  CRUD lifecycle of `/resources` plus the two health endpoints over real HTTP.
- Adding the new test project to `Abm.PD.slnx`.

Out of scope (explicitly deferred, not forgotten):
- CI wiring — no `.github/workflows` exists yet in this repo.
- OpenTelemetry instrumentation and trace filtering for the health-check paths. Health checks and
  OpenTelemetry are separate concerns (health contract vs. traces/metrics/logs export); the only
  place they'd touch is filtering `/health/*` out of trace sampling once OTel exists. Noted here for
  whoever adds OTel later, not built now.
- Full IETF `health+json` (draft-inadarei-api-health-check) compliance — versioning, `serviceId`,
  etc. ASP.NET Core's own structured JSON shape is close enough without hand-rolling the full draft.
- A container-based test suite that runs the actual built API image (would validate the Dockerfile
  itself and container startup behaviour, at the cost of debuggability and run speed). Not built now;
  worth revisiting later as a separate, less-frequently-run deployment smoke check if the Dockerfile
  ever needs that level of verification.
- Testing anything beyond `Abm.PD.Core.Api` (no bulk-export/loader integration tests here).

## Health endpoints (`Abm.PD.Core.Api`)

- New packages: `Microsoft.Extensions.Diagnostics.HealthChecks` (ships with the ASP.NET Core shared
  framework, no extra reference needed) and `AspNetCore.HealthChecks.NpgSql` (the community package
  for the Postgres check).
- `Program.cs`: `builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres", tags: ["ready"])`.
  Reuses the same `connectionString` already resolved for `AddCoreRepositoryServices` — not a second
  independent read of configuration.
- Liveness — deliberately checks nothing downstream, so a Postgres blip never causes a liveness
  probe to fail and the process to be killed/restarted:
  ```csharp
  app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
  ```
- Readiness — runs only checks tagged `ready` (today, just Postgres):
  ```csharp
  app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
  ```
- Both endpoints share one `ResponseWriter` that serialises the `HealthReport` to JSON:
  `{ status, totalDuration, entries: { name: { status, description, duration } } }` — ASP.NET Core's
  conventional structured shape, not the bare `"Healthy"`/`"Unhealthy"` string the middleware returns
  by default.
- Lives in a new `Endpoints/HealthCheckEndpoints.cs`, alongside `ResourceEndpoints.cs`, mapped from
  `Program.cs` the same way (`app.MapHealthCheckEndpoints()`).
- No auth on these endpoints — matches `ResourceEndpoints` today (no auth exists anywhere in
  `Abm.PD.Core.Api` yet); not introduced here as a side effect.

## Dockerfile (`Abm.PD.Core.Api/Dockerfile`)

Mirrors `Abm.Pyro.Api/Dockerfile` exactly in structure, adjusted for this project's name and its two
project references:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Abm.PD.Core.Api/Abm.PD.Core.Api.csproj", "Abm.PD.Core.Api/"]
COPY ["Abm.PD.Core.Domain/Abm.PD.Core.Domain.csproj", "Abm.PD.Core.Domain/"]
COPY ["Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj", "Abm.PD.Core.Repository/"]
RUN dotnet restore "Abm.PD.Core.Api/Abm.PD.Core.Api.csproj"
COPY . .
WORKDIR "/src/Abm.PD.Core.Api"
RUN dotnet build "Abm.PD.Core.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Abm.PD.Core.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Abm.PD.Core.Api.dll"]
```

`aspnet:10.0-noble-chiseled-extra` — the chiseled Ubuntu Noble base, same choice as `PyroServer`:
minimal attack surface/image size versus the full `aspnet` image, "extra" variant needed for ICU/
globalization support. Build context is `src/Abm.PD/` (the solution folder), not `Abm.PD.Core.Api/`
itself, matching `Abm.Pyro.Api/Dockerfile`'s equivalent multi-project `COPY` layout — required because
the project references sibling projects via `ProjectReference`. Not exercised by
`Abm.PD.Core.Api.Tests` — purely a deployment artifact, validated only by `docker build` succeeding.

## `Abm.PD.Core.Api.Tests`

New project at `src/Abm.PD/Abm.PD.Core.Api.Tests/`, added to `Abm.PD.slnx`. Same conventions as the
existing test projects (`Abm.PD.Tests`, `Abm.PD.BulkExport.Tests`): xunit, no third-party assertion
libraries, `net10.0`, `ImplicitUsings`/`Nullable` enabled, no `TreatWarningsAsErrors`.

Packages: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` (versions matching the
existing test projects), plus `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`, and
`Respawn`.

Project references: `Abm.PD.Core.Api` (for `WebApplicationFactory<Program>` and `ResourceRequest`),
`Abm.PD.Core.Domain` (for `Resource`), and `Abm.PD.Core.Repository` (for `ProviderDirectoryDbContext`,
needed to run migrations against the container in the fixture).

### Test infrastructure

Directly follows `Abm.Pyro.Api.Test/Fixtures/`'s file layout and responsibilities:

- **`Fixtures/IntegrationTestFixture.cs`** (`IAsyncLifetime`, shared once per run via
  `ICollectionFixture`):
  1. Start a `PostgreSqlBuilder().Build()` container.
  2. Run EF Core migrations against it directly: `new DbContextOptionsBuilder<ProviderDirectoryDbContext>().UseNpgsql(connectionString)`,
     `await context.Database.MigrateAsync()`.
  3. Create a `Respawner` checkpoint against the migrated, empty database (`DbAdapter.Postgres`,
     covering the `resource` table — the only one today; more tables get added to
     `TablesToInclude` as the schema grows).
  4. Construct `CoreApiWebApplicationFactory` (the connection string passed to its constructor) and
     call `Factory.CreateClient()` for the shared `HttpClient`.
  5. `DisposeAsync`: dispose the client, the factory, then the Postgres container.
- **`Fixtures/CoreApiWebApplicationFactory.cs`** (`WebApplicationFactory<Program>`):
  `ConfigureWebHost` calls `ConfigureAppConfiguration` with an in-memory collection setting
  `ConnectionStrings:ProviderDirectoryDb` to the container's connection string and
  `Database:RunMigrationsOnStartup` to `"false"` — migration already happened once in the fixture;
  the app must not also try to migrate on every `WebApplicationFactory` boot.
- **`Fixtures/IntegrationTestCollection.cs`** — `[CollectionDefinition(nameof(IntegrationTestCollection))]`
  marker class implementing `ICollectionFixture<IntegrationTestFixture>`.
- **`Fixtures/IntegrationTestBase.cs`** — `[Collection(nameof(IntegrationTestCollection))]`, abstract,
  constructor takes `IntegrationTestFixture`. `IAsyncLifetime.InitializeAsync` calls
  `Fixture.ResetDatabaseAsync()` — runs before every `[Fact]`, since xunit creates a fresh test-class
  instance per test method. Exposes `protected HttpClient HttpClient => Fixture.HttpClient;` for test
  classes to use directly (no FHIR client wrapper needed here, unlike PyroServer).

Test classes derive from `IntegrationTestBase`, e.g. `public class ResourceCrudTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)`.

### What's tested

The full CRUD lifecycle of `/resources`, plus the health endpoints, through real HTTP → the
in-process API → real Postgres:

- `POST /resources` → 201, body round-trips the created resource.
- `GET /resources/{id}` → 200, matches what was created.
- `GET /resources` → contains the created resource.
- `GET /resources/search?resourceType=...&resourceId=...` → finds it.
- `PUT /resources/{id}` → 200, updated fields persist.
- `DELETE /resources/{id}` → 204.
- `GET /resources/{id}` after delete → 404.
- `PUT /resources/{id}` on a non-existent id → 404.
- `GET /health/live` → 200, always (no DB dependency).
- `GET /health/ready` → 200, with the Postgres container up.

## Error handling

No custom error handling introduced. A container that fails to start, or a migration that fails,
fails the fixture's `InitializeAsync`, which fails every test in the collection — correct fail-fast
behaviour for a shared per-run fixture; no retry/recovery logic beyond what Testcontainers' own
readiness wait already provides.

## Solution/build

`Abm.PD.slnx` gains one `<Project Path="Abm.PD.Core.Api.Tests/Abm.PD.Core.Api.Tests.csproj" />` entry.
`Abm.PD.Core.Api/Program.cs` gains `public partial class Program { }` at the end, required for
`WebApplicationFactory<Program>` to reference the entry point type. `dotnet build src/Abm.PD/Abm.PD.slnx`
must succeed with all eight projects. Running the new test project requires a local Docker daemon
(confirmed available: Docker 29.7.2) for the Postgres container — this is a genuine integration test
project, not something that runs in an environment without Docker.

## Branch

`feature/core-api-integration-tests`, off `development`.
