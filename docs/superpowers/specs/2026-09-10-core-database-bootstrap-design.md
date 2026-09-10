# Core database bootstrap: Abm.PD.Core.Api / Core.Domain / Core.Repository

Date: 2026-09-10
Status: Approved for planning

## Purpose

Add a new, independent web application and persistence stack to the `Abm.PD.slnx` solution,
alongside the existing bulk-export tooling (`Abm.PD.BulkExport`, `Abm.PD.Console`), establishing
the clean-architecture layering and EF Core / PostgreSQL conventions the eventual local provider
directory repository will be built on. This piece of work delivers a single stub `Resource` table
and a proof-of-life HTTP endpoint — no real domain entities, no wiring to the existing bulk-export
or load pipeline.

## Scope

In scope:
- Three new projects: `Abm.PD.Core.Domain`, `Abm.PD.Core.Repository`, `Abm.PD.Core.Api`.
- One entity (`Resource`: `Id`, `ResourceType`, `ResourceId`), one repository interface/implementation,
  one EF Core migration, one HTTP endpoint.
- Migration-on-startup toggle, environment-appropriate defaults.
- PostgreSQL/EF Core conventions decided now because they are expensive to retrofit after migrations
  exist: snake_case naming, `text` over `varchar(n)`, retry-on-failure and its execution-strategy
  implication for future transactional code.

Out of scope (explicitly deferred, not forgotten):
- Docker Compose for Postgres — already exists outside this repo at
  `C:\Temp\DockerCompose\PostgresSQL\compose.yaml`, not something this repo needs to own.
- Automated tests for this bootstrap step.
- Wiring the new database into `FhirBatchLoader` / the bulk-export console pipeline.
- Authentication/authorisation on the Api.
- Optimistic concurrency (`xmin`), enum column mapping, anything else not needed by a one-column stub.

## Solution layout

Three new project folders as siblings of the existing ones under `src/Abm.PD/`, all added to
`Abm.PD.slnx`. `Abm.PD.BulkExport`, `Abm.PD.Console`, and `Abm.PD.Tests` are unaffected.

```
src/Abm.PD/
  Abm.PD.Core.Domain/       Entities + repository interfaces. No dependencies on the other two.
  Abm.PD.Core.Repository/   EF Core DbContext, migrations, repository implementations. Depends on Core.Domain.
  Abm.PD.Core.Api/          ASP.NET Core minimal API host. Depends on Core.Domain and Core.Repository.
```

All three target `net10.0` with `ImplicitUsings`/`Nullable` enabled, matching the rest of the
solution. `Abm.PD.Core.Api` sets `TreatWarningsAsErrors`, matching `Abm.PD.Console`'s convention for
the executable/host project; the two library projects do not, matching `Abm.PD.BulkExport`.

## Abm.PD.Core.Domain

- `Entities/Resource.cs` — a plain class, not a record: `int Id`, `string ResourceType`,
  `string ResourceId`. EF Core change tracking wants mutable properties; this is a deliberate,
  narrow exception to the repo's usual record-for-data convention, scoped to EF Core entity types.
- `Repositories/IResourceRepository.cs` — `Task<IReadOnlyList<Resource>> GetAllAsync(CancellationToken ct)`.
  Only what the one endpoint needs; no `Add`/`Update`/`Delete` until something requires them.

No package dependencies beyond the BCL.

## Abm.PD.Core.Repository

- `ProviderDirectoryDbContext : DbContext` — `DbSet<Resource> Resources`. `OnModelCreating` explicitly
  calls `modelBuilder.Entity<Resource>().ToTable("resource")` — EF Core's default table-naming
  convention takes the `DbSet` property name (`Resources`, plural), which snake-casing alone would
  turn into `resources`; the explicit `ToTable` keeps the table singular, matching the entity name.
- `ResourceRepository : IResourceRepository` — implemented against the `DbContext`.
- `Migrations/` — the initial migration, creating the `resource` table (see naming convention below).
- `DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProviderDirectoryDbContext>` — reads the
  connection string from the `ConnectionStrings__ProviderDirectoryDb` environment variable, falling
  back to the literal local Docker Compose value
  (`Host=localhost;Port=5432;Database=provider-directory;Username=admin;Password=admin`) when unset.
  That value is a throwaway local-dev credential (see `compose.yaml` above), not a production secret,
  so hard-coding it as a fallback is acceptable. This lets
  `dotnet ef migrations add --project Abm.PD.Core.Repository` run standalone without needing the Api
  host or its configuration.
- `DependencyInjection/ServiceCollectionExtension.cs` — `AddCoreRepositoryServices(IServiceCollection, IConfiguration)`
  registers `ProviderDirectoryDbContext` via `UseNpgsql(connectionString, o => ...)` and
  `IResourceRepository → ResourceRepository`.

Packages: `Microsoft.EntityFrameworkCore` (>= 10.0.4), `Microsoft.EntityFrameworkCore.Design`,
`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `EFCore.NamingConventions`.

### PostgreSQL / EF Core conventions decided now

These are decided at this stage specifically because they are cheap before the first migration
exists and expensive after (renaming every table/column in a follow-up migration).

- **Snake_case naming (`EFCore.NamingConventions`)** — `UseNpgsql(...).UseSnakeCaseNamingConvention()`
  in `AddCoreRepositoryServices`. Postgres folds unquoted identifiers to lowercase; without this,
  EF's default `PascalCase` table/column names require exact-case quoting (`"ResourceType"`) in every
  raw SQL query, `psql` session, or external tool, which is the idiom mismatch this package removes.
  Applies to table, column, index, and foreign-key names alike.
- **`text` over `varchar(n)`** — `string` properties map to Postgres `text` by default; do not impose
  `varchar(n)` length limits unless a real business rule requires one. Postgres has no performance
  difference between the two (`varchar(n)` is `text` plus a length check).
- **No NodaTime plugin** — the stated need is `DateTimeOffset`/`DateTime` only, which the base
  `Npgsql.EntityFrameworkCore.PostgreSQL` provider already maps natively (`DateTimeOffset` →
  `timestamptz`, `DateTime` → `timestamp without time zone`, subject to `Kind` matching). No entity in
  this bootstrap has a date/time column, but this is the standing guidance for when one is added:
  prefer `DateTimeOffset` for instants, store as UTC.
- **`EnableRetryOnFailure()`** — enabled with framework defaults in `AddCoreRepositoryServices`
  (`o.EnableRetryOnFailure()`). Inert against the local single-container Postgres today, but this is
  intended to become critical production infrastructure on managed cloud Postgres, where transient
  failover/throttling errors are exactly what this exists to absorb. Decided now, before any
  transactional code exists, because of its binding constraint: once an execution strategy is
  configured, EF Core forbids a bare `context.Database.BeginTransaction()` — any future manual
  transaction (plausible once this database starts receiving batched loads, given the
  `FhirBatchLoader` batch-commit pattern already in this codebase) must be wrapped in
  `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`. Nothing in this bootstrap opens a
  manual transaction, so there is nothing to change today — this is a note for whoever adds the first
  one.
- **Deferred, not adopted**: `xmin`-based optimistic concurrency tokens (nothing here needs
  concurrency protection yet) and enum column mapping (not applicable — `ResourceType` is a plain
  string).

## Abm.PD.Core.Api

- `Program.cs` — `WebApplicationBuilder`; `UseSerilog()` via `Serilog.AspNetCore` (reads the same
  `Serilog` config-section shape as `Abm.PD.Console`); calls `AddCoreRepositoryServices`. After
  `app.Build()`, if configuration key `Database:RunMigrationsOnStartup` is `true`, resolves
  `ProviderDirectoryDbContext` in a scope and calls `Database.MigrateAsync()` before `app.Run()`.
- One endpoint: `app.MapGet("/resources", ...)` → `IResourceRepository.GetAllAsync()` — proves
  Api → Repository → Postgres end-to-end.
- `appsettings.json` (committed): `"Database": { "RunMigrationsOnStartup": false }`,
  `"ConnectionStrings": { "ProviderDirectoryDb": "" }` (empty).
- `appsettings.Development.json` (committed): `"Database": { "RunMigrationsOnStartup": true }`.
- New `UserSecretsId`. The actual connection string
  (`Host=localhost;Port=5432;Database=provider-directory;Username=admin;Password=admin`) is set via
  `dotnet user-secrets set "ConnectionStrings:ProviderDirectoryDb" "..."` against the Api project, not
  committed — matching how the SIT bearer token is already handled for `Abm.PD.Console`.

Packages: `Serilog.AspNetCore`, plus the same `Microsoft.Extensions.*` / `Serilog.Sinks.*` packages
`Abm.PD.Console` uses for configuration binding and file/console sinks.

## Error handling

None beyond default ASP.NET Core behaviour. A bad connection string or a failed migration crashes
startup — correct for a fail-fast bootstrap; no custom exception-handling middleware at this stage.

## Testing

None for this step, per explicit decision — this is a stub to bootstrap the setup. Test coverage
starts once real entities/logic land.

## Solution/build

`Abm.PD.slnx` gains three `<Project Path=".../....csproj" />` entries. `dotnet build src/Abm.PD/Abm.PD.slnx`
must succeed with all seven projects (four existing + three new).
