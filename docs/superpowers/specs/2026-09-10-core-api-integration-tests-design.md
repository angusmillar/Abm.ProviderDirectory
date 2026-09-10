# Core.Api end-to-end integration tests + health endpoints

Date: 2026-09-10
Status: Approved for planning

## Purpose

Add a new integration test project, `Abm.PD.Core.Api.Tests`, that exercises `Abm.PD.Core.Api`
completely end-to-end: a real Docker image of the API, talking to a real, disposable PostgreSQL
container, driven entirely over HTTP. No `WebApplicationFactory`, no in-process hosting, no faked
persistence — the same artifact that would actually be deployed, started and torn down for the
test run.

Alongside this, add production `/health/live` and `/health/ready` endpoints to `Abm.PD.Core.Api`
itself. These are useful now (container orchestration, future Kubernetes probes) independent of the
test project, and the test project's container-readiness wait strategy uses `/health/ready` as its
signal — so the two pieces are delivered together.

## Scope

In scope:
- A new `Dockerfile` for `Abm.PD.Core.Api` (none exists today), suitable both for this test project
  and as the real deployment artifact.
- `/health/live` and `/health/ready` endpoints on `Abm.PD.Core.Api`, using
  `Microsoft.Extensions.Diagnostics.HealthChecks` + `AspNetCore.HealthChecks.NpgSql`.
- A new `Abm.PD.Core.Api.Tests` xunit project using Testcontainers for .NET to run a Postgres
  container and an API container (built from the new Dockerfile) per test run, and exercise the full
  CRUD lifecycle of `/resources` over real HTTP.
- Adding the new test project to `Abm.PD.slnx`.

Out of scope (explicitly deferred, not forgotten):
- CI wiring — no `.github/workflows` exists yet in this repo.
- OpenTelemetry instrumentation and trace filtering for the health-check paths. Health checks and
  OpenTelemetry are separate concerns (health contract vs. traces/metrics/logs export); the only
  place they'd touch is filtering `/health/*` out of trace sampling once OTel exists. Noted here for
  whoever adds OTel later, not built now.
- Full IETF `health+json` (draft-inadarei-api-health-check) compliance — versioning, `serviceId`,
  etc. ASP.NET Core's own structured JSON shape is close enough without hand-rolling the full draft.
- Resetting/truncating the database between tests — each test uses unique, GUID-suffixed data
  instead (see Test data isolation below), so no reset machinery is needed.
- Testing anything beyond `Abm.PD.Core.Api` (no bulk-export/loader integration tests here).

## Health endpoints (`Abm.PD.Core.Api`)

- New packages: `Microsoft.Extensions.Diagnostics.HealthChecks` (ships with the ASP.NET Core shared
  framework, no extra reference needed for the core API) and `AspNetCore.HealthChecks.NpgSql` (the
  community package for the Postgres check).
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

Standard multi-stage ASP.NET Core build:
1. `mcr.microsoft.com/dotnet/sdk:10.0` — restore and publish `Abm.PD.Core.Api.csproj`.
2. `mcr.microsoft.com/dotnet/aspnet:10.0` — copy the publish output, `ENTRYPOINT` the app.

Build context must be `src/Abm.PD/` (the solution folder), not `Abm.PD.Core.Api/` itself, because the
project references sibling projects (`Abm.PD.Core.Domain`, `Abm.PD.Core.Repository`) via
`ProjectReference` — the Dockerfile needs the whole source tree available to `dotnet restore`/`publish`.
This is the real deployment artifact going forward, not solely a test fixture.

## `Abm.PD.Core.Api.Tests`

New project at `src/Abm.PD/Abm.PD.Core.Api.Tests/`, added to `Abm.PD.slnx`. Same conventions as the
existing test projects (`Abm.PD.Tests`, `Abm.PD.BulkExport.Tests`): xunit, no third-party mocking or
assertion libraries, `net10.0`, `ImplicitUsings`/`Nullable` enabled, no `TreatWarningsAsErrors`.

Packages: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` (versions matching the
existing test projects), plus `Testcontainers` and `Testcontainers.PostgreSql`.

Project references: `Abm.PD.Core.Api` (for `ResourceRequest`) and `Abm.PD.Core.Domain` (for
`Resource`) — tests build typed requests and deserialise typed responses rather than hand-building
JSON, even though the actual assertions travel over real HTTP.

### Container lifecycle

One `ApiTestFixture : IAsyncLifetime`, shared across the whole test run via an xunit
`ICollectionFixture` (`[CollectionDefinition]` + `[Collection]` on every test class, so all test
classes share one Postgres + one API container rather than spinning up fresh ones per class).

`InitializeAsync`:
1. Create a Testcontainers `INetwork`.
2. Start `PostgreSqlBuilder`'s container on that network with alias `postgres-test`, using the module's
   built-in readiness wait.
3. Build the API image from the Dockerfile via `ImageFromDockerfileBuilder` (context `src/Abm.PD/`,
   dockerfile `Abm.PD.Core.Api/Dockerfile`).
4. Start the API container on the same network with:
   - `ConnectionStrings__ProviderDirectoryDb` = `Host=postgres-test;Port=5432;Database=...;Username=...;Password=...`
     (container-to-container, via the network alias — not the Postgres container's host-mapped port)
   - `Database__RunMigrationsOnStartup=true` — same mechanism `appsettings.Development.json` already
     uses; migrations run automatically before the app starts listening, no separate migration step
   - A wait strategy polling `GET /health/ready` until it returns 200
5. Expose the API container's port to the host; fixture exposes an `HttpClient` (or base address)
   pointed at the mapped `http://localhost:{port}` for tests to use.

`DisposeAsync`: stop/remove both containers and the network. Testcontainers' Ryuk reaper is the
backstop if a run is killed mid-way, so nothing leaks even on a crashed test run.

### Test data isolation

Each test generates its own GUID-suffixed `ResourceType`/`ResourceId` (e.g. a small
`TestData.UniqueResource()` helper), so tests never collide against the shared instance and need no
database reset between tests, and can run in parallel or in any order.

### What's tested

The full CRUD lifecycle of `/resources` through real HTTP → real API process → real Postgres:
- `POST /resources` → 201, body round-trips the created resource.
- `GET /resources/{id}` → 200, matches what was created.
- `GET /resources` → contains the created resource.
- `GET /resources/search?resourceType=...&resourceId=...` → finds it.
- `PUT /resources/{id}` → 200, updated fields persist.
- `DELETE /resources/{id}` → 204.
- `GET /resources/{id}` after delete → 404.
- `PUT /resources/{id}` on a non-existent id → 404.

## Error handling

No custom error handling introduced. A container that fails to become healthy within the wait
strategy's timeout fails the fixture's `InitializeAsync`, which fails every test in the collection —
correct fail-fast behaviour for a shared per-run fixture; no retry/recovery logic beyond what
Testcontainers' wait strategies already provide.

## Solution/build

`Abm.PD.slnx` gains one `<Project Path="Abm.PD.Core.Api.Tests/Abm.PD.Core.Api.Tests.csproj" />` entry.
`dotnet build src/Abm.PD/Abm.PD.slnx` must succeed with all eight projects. Running the new test
project requires a local Docker daemon (confirmed available: Docker 29.7.2) — this is a genuine
integration test project, not something that runs in an environment without Docker.

## Branch

`feature/core-api-integration-tests`, off `development`.
