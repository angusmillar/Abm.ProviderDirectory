# Core Database Bootstrap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `Abm.PD.Core.Domain`, `Abm.PD.Core.Repository`, and `Abm.PD.Core.Api` to `Abm.PD.slnx`, giving the solution a clean-architecture EF Core / PostgreSQL persistence stack with one stub `Resource` table and one proof-of-life HTTP endpoint.

**Architecture:** `Core.Domain` (entity + repository interface, no dependencies) ← `Core.Repository` (EF Core `DbContext`, migrations, repository implementation, Npgsql) ← `Core.Api` (ASP.NET Core minimal API host, DI wiring, migration-on-startup toggle, `/resources` endpoint). Existing `Abm.PD.BulkExport` / `Abm.PD.Console` / `Abm.PD.Tests` are untouched.

**Tech Stack:** .NET 10 (`net10.0`), ASP.NET Core minimal APIs, EF Core 10, `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`, `Serilog.AspNetCore`, PostgreSQL 18 (local Docker container).

**Spec:** `docs/superpowers/specs/2026-09-10-core-database-bootstrap-design.md`

## Global Constraints

- Target framework `net10.0` on all three new projects, with `ImplicitUsings` and `Nullable` both `enable` — matches every existing project in the solution.
- `Abm.PD.Core.Api` sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — matches `Abm.PD.Console`'s convention for the executable/host project. `Core.Domain` and `Core.Repository` do not set it, matching `Abm.PD.BulkExport`.
- No automated tests for this piece of work (explicit spec decision). Every task's verification is a manual build/run check instead of a TDD red/green cycle.
- No secrets committed to any tracked `appsettings*.json`. The real Postgres connection string only ever goes into `Abm.PD.Core.Api`'s user secrets, via `dotnet user-secrets set`.
- Snake_case naming convention (`EFCore.NamingConventions` → `UseSnakeCaseNamingConvention()`) applied wherever the Npgsql provider is configured (`AddCoreRepositoryServices` and `DesignTimeDbContextFactory` both call it).
- The `Resource` entity table is explicitly named `resource` (singular) via `modelBuilder.Entity<Resource>().ToTable("resource")` — EF Core's default table-naming convention would otherwise take the `DbSet` property name (`Resources`), snake-cased to `resources` (plural).
- `string` properties map to Postgres `text` (the EF Core/Npgsql default) — no `[MaxLength]`/`varchar(n)` unless a real business rule needs one. None do yet.
- `EnableRetryOnFailure()` (framework defaults) is enabled on the Npgsql provider registration in `AddCoreRepositoryServices`. No manual `BeginTransaction()` exists anywhere in this plan, so the execution-strategy constraint that comes with it (manual transactions must go through `CreateExecutionStrategy().ExecuteAsync(...)`) has nothing to violate yet — it's simply in place for whoever adds the first one.
- Local Postgres connection string (from `C:\Temp\DockerCompose\PostgresSQL\compose.yaml`): `Host=localhost;Port=5432;Database=provider-directory;Username=admin;Password=admin`.
- File-scoped namespaces; explicit types over `var`; Australian English in comments/log messages; XML doc comments only where they explain a non-obvious *why* (per `CLAUDE.md`).
- Package versions: do not pin an exact version by hand in any `PackageReference` you type manually — always add packages via `dotnet add package <id>` (no `--version`) so NuGet resolves and pins whatever is currently latest-stable for `net10.0`; then note the resolved version in your step's result.

---

### Task 1: Abm.PD.Core.Domain — Resource entity and repository interface

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Domain/Abm.PD.Core.Domain.csproj`
- Create: `src/Abm.PD/Abm.PD.Core.Domain/Entities/Resource.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Domain/Repositories/IResourceRepository.cs`
- Modify: `src/Abm.PD/Abm.PD.slnx`

**Interfaces:**
- Produces: `Abm.PD.Core.Domain.Entities.Resource` — plain class, `int Id { get; set; }`, `required string ResourceType { get; set; }`, `required string ResourceId { get; set; }`.
- Produces: `Abm.PD.Core.Domain.Repositories.IResourceRepository` — `Task<IReadOnlyList<Resource>> GetAllAsync(CancellationToken cancellationToken)`.

- [ ] **Step 1: Scaffold the class library**

Run:
```bash
dotnet new classlib -n Abm.PD.Core.Domain -o src/Abm.PD/Abm.PD.Core.Domain
rm src/Abm.PD/Abm.PD.Core.Domain/Class1.cs
```
Expected: `src/Abm.PD/Abm.PD.Core.Domain/Abm.PD.Core.Domain.csproj` exists with `<TargetFramework>net10.0</TargetFramework>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<Nullable>enable</Nullable>`. If the scaffolded `TargetFramework` is not `net10.0`, edit the `.csproj` to set it explicitly.

- [ ] **Step 2: Register the project in the solution**

Open `src/Abm.PD/Abm.PD.slnx` and add a new `<Project Path="..." />` line so the file reads:
```xml
<Solution>
  <Project Path="Abm.PD.BulkExport/Abm.PD.BulkExport.csproj" />
  <Project Path="Abm.PD.Console/Abm.PD.Console.csproj" />
  <Project Path="Abm.PD.Core.Domain/Abm.PD.Core.Domain.csproj" />
  <Project Path="Abm.PD.Tests/Abm.PD.Tests.csproj" />
</Solution>
```

- [ ] **Step 3: Write the Resource entity**

Create `src/Abm.PD/Abm.PD.Core.Domain/Entities/Resource.cs`:
```csharp
namespace Abm.PD.Core.Domain.Entities;

/// <summary>
/// Stub entity bootstrapping the provider directory database. Not yet backed by a specific FHIR
/// resource shape — ResourceType/ResourceId simply record what the resource is and its source
/// identifier, ahead of the real entity model this table will grow into.
/// </summary>
public class Resource
{
    public int Id { get; set; }

    public required string ResourceType { get; set; }

    public required string ResourceId { get; set; }
}
```

- [ ] **Step 4: Write the repository interface**

Create `src/Abm.PD/Abm.PD.Core.Domain/Repositories/IResourceRepository.cs`:
```csharp
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Domain.Repositories;

public interface IResourceRepository
{
    Task<IReadOnlyList<Resource>> GetAllAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: Build succeeds, `Abm.PD.Core.Domain` listed among the built projects, no warnings/errors.

- [ ] **Step 6: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Domain src/Abm.PD/Abm.PD.slnx
git commit -m "Add Abm.PD.Core.Domain with the stub Resource entity and repository interface

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HVaKXvHUdjst4jhTJVKyKg"
```

---

### Task 2: Abm.PD.Core.Repository — DbContext, EF Core wiring, DI extension

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/ResourceRepository.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/DesignTimeDbContextFactory.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs`
- Modify: `src/Abm.PD/Abm.PD.slnx`

**Interfaces:**
- Consumes: `Abm.PD.Core.Domain.Entities.Resource`, `Abm.PD.Core.Domain.Repositories.IResourceRepository` (Task 1).
- Produces: `Abm.PD.Core.Repository.ProviderDirectoryDbContext` — `public ProviderDirectoryDbContext(DbContextOptions<ProviderDirectoryDbContext> options)`, `DbSet<Resource> Resources`.
- Produces: `Abm.PD.Core.Repository.ResourceRepository : IResourceRepository`.
- Produces: `Abm.PD.Core.Repository.DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProviderDirectoryDbContext>`.
- Produces: `Abm.PD.Core.Repository.DependencyInjection.ServiceCollectionExtension.AddCoreRepositoryServices(this IServiceCollection services, IConfiguration configuration) : IServiceCollection` — reads the connection string from configuration key `ConnectionStrings:ProviderDirectoryDb`, registers `ProviderDirectoryDbContext` (Npgsql, snake_case naming, retry-on-failure) and `IResourceRepository → ResourceRepository`.

- [ ] **Step 1: Scaffold the class library**

Run:
```bash
dotnet new classlib -n Abm.PD.Core.Repository -o src/Abm.PD/Abm.PD.Core.Repository
rm src/Abm.PD/Abm.PD.Core.Repository/Class1.cs
```
Expected: same `net10.0`/`ImplicitUsings`/`Nullable` shape as Task 1. Fix the `.csproj` by hand if `TargetFramework` did not come out as `net10.0`.

- [ ] **Step 2: Add package references and the project reference to Core.Domain**

Run:
```bash
dotnet add src/Abm.PD/Abm.PD.Core.Repository package Microsoft.EntityFrameworkCore
dotnet add src/Abm.PD/Abm.PD.Core.Repository package Microsoft.EntityFrameworkCore.Design
dotnet add src/Abm.PD/Abm.PD.Core.Repository package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Abm.PD/Abm.PD.Core.Repository package EFCore.NamingConventions
dotnet add src/Abm.PD/Abm.PD.Core.Repository reference src/Abm.PD/Abm.PD.Core.Domain
```
Expected: `Abm.PD.Core.Repository.csproj` now has four `<PackageReference>` entries (each with whatever exact version NuGet resolved) and one `<ProjectReference Include="..\Abm.PD.Core.Domain\Abm.PD.Core.Domain.csproj" />`.

`Microsoft.EntityFrameworkCore.Design` is a build-time tooling package only — mark it so it doesn't leak as a runtime dependency to projects that reference `Abm.PD.Core.Repository` (i.e. `Abm.PD.Core.Api`). Edit its `<PackageReference>` line in the `.csproj` to:
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="...">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```
(keep whatever `Version` `dotnet add package` resolved).

- [ ] **Step 3: Register the project in the solution**

Add to `src/Abm.PD/Abm.PD.slnx` (alongside the entry from Task 1):
```xml
<Project Path="Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj" />
```

- [ ] **Step 4: Write the DbContext**

Create `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`:
```csharp
using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository;

public class ProviderDirectoryDbContext(DbContextOptions<ProviderDirectoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Resource> Resources => Set<Resource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // EF Core's default table-naming convention takes the DbSet property name (Resources,
        // plural); snake-casing alone would produce "resources". Pin it singular explicitly.
        modelBuilder.Entity<Resource>().ToTable("resource");
    }
}
```

- [ ] **Step 5: Write the repository implementation**

Create `src/Abm.PD/Abm.PD.Core.Repository/ResourceRepository.cs`:
```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository;

public class ResourceRepository(ProviderDirectoryDbContext dbContext) : IResourceRepository
{
    public async Task<IReadOnlyList<Resource>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Resources
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
```

- [ ] **Step 6: Write the design-time factory**

Create `src/Abm.PD/Abm.PD.Core.Repository/DesignTimeDbContextFactory.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Abm.PD.Core.Repository;

/// <summary>
/// Lets `dotnet ef` commands run against this project standalone, without needing
/// Abm.PD.Core.Api's host or configuration. The connection string comes from the
/// ConnectionStrings__ProviderDirectoryDb environment variable, falling back to the local Docker
/// Compose default below — a throwaway local-dev credential (see
/// C:\Temp\DockerCompose\PostgresSQL\compose.yaml), not a production secret.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProviderDirectoryDbContext>
{
    private const string FallbackDevConnectionString =
        "Host=localhost;Port=5432;Database=provider-directory;Username=admin;Password=admin";

    public ProviderDirectoryDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ProviderDirectoryDb")
            ?? FallbackDevConnectionString;

        DbContextOptionsBuilder<ProviderDirectoryDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

        return new ProviderDirectoryDbContext(optionsBuilder.Options);
    }
}
```

- [ ] **Step 7: Write the DI extension**

Create `src/Abm.PD/Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs`:
```csharp
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Repository.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddCoreRepositoryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("ProviderDirectoryDb")
            ?? throw new InvalidOperationException(
                "Missing required connection string 'ConnectionStrings:ProviderDirectoryDb'.");

        services.AddDbContext<ProviderDirectoryDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IResourceRepository, ResourceRepository>();

        return services;
    }
}
```

- [ ] **Step 8: Build and verify**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: Build succeeds, `Abm.PD.Core.Repository` listed among the built projects, no warnings/errors.

- [ ] **Step 9: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Repository src/Abm.PD/Abm.PD.slnx
git commit -m "Add Abm.PD.Core.Repository with EF Core Npgsql DbContext and DI wiring

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HVaKXvHUdjst4jhTJVKyKg"
```

---

### Task 3: Initial EF Core migration, applied to the local database

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/<timestamp>_InitialCreate.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/<timestamp>_InitialCreate.Designer.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/ProviderDirectoryDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: `Abm.PD.Core.Repository.ProviderDirectoryDbContext`, `Abm.PD.Core.Repository.DesignTimeDbContextFactory` (Task 2). No new C# types are produced for later tasks to reference — this task's deliverable is the migration files plus the applied schema in the running Postgres container.

- [ ] **Step 1: Confirm the local Postgres container is running**

Run: `docker ps --filter "name=postgres-dev"`
Expected: one row for container `postgres-dev`, status `Up`. If it's not running, start it: `docker compose -f C:\Temp\DockerCompose\PostgresSQL\compose.yaml up -d`

- [ ] **Step 2: Generate the initial migration**

Run:
```bash
dotnet ef migrations add InitialCreate --project src/Abm.PD/Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj
```
Expected: Output ends with `Done.`; three new files appear under `src/Abm.PD/Abm.PD.Core.Repository/Migrations/`. `dotnet-ef` uses `DesignTimeDbContextFactory` (Task 2 Step 6) to construct the context — no environment variable is required, since its fallback connection string already matches the running container.

- [ ] **Step 3: Inspect the generated migration for the naming convention**

Open the new `<timestamp>_InitialCreate.cs` and confirm the `Up` method contains `migrationBuilder.CreateTable(name: "resource", ...)` with three snake_case columns: `id`, `resource_type`, `resource_id`. If any name is not snake_case or the table is not singular `resource`, `UseSnakeCaseNamingConvention()` or the `ToTable("resource")` call from Task 2 was missed — fix `ProviderDirectoryDbContext`/`DesignTimeDbContextFactory`, delete the migration (`dotnet ef migrations remove --project src/Abm.PD/Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj`), and regenerate.

- [ ] **Step 4: Apply the migration to the local database**

Run:
```bash
dotnet ef database update --project src/Abm.PD/Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj
```
Expected: Output ends with `Done.`, no errors connecting to `localhost:5432`.

- [ ] **Step 5: Verify the table exists in Postgres**

Run:
```bash
docker exec postgres-dev psql -U admin -d provider-directory -c "\d resource"
```
Expected: a table description listing columns `id` (integer, not null), `resource_type` (text, not null), `resource_id` (text, not null).

- [ ] **Step 6: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Repository/Migrations
git commit -m "Add initial EF Core migration creating the resource table

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HVaKXvHUdjst4jhTJVKyKg"
```

---

### Task 4: Abm.PD.Core.Api — host, migration-on-startup, /resources endpoint

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Api/Abm.PD.Core.Api.csproj`
- Create: `src/Abm.PD/Abm.PD.Core.Api/Program.cs` (overwrite the scaffolded template)
- Create: `src/Abm.PD/Abm.PD.Core.Api/Settings/DatabaseSettings.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Api/appsettings.json` (overwrite)
- Create: `src/Abm.PD/Abm.PD.Core.Api/appsettings.Development.json` (overwrite)
- Modify: `src/Abm.PD/Abm.PD.slnx`

**Interfaces:**
- Consumes: `Abm.PD.Core.Domain.Repositories.IResourceRepository` (Task 1); `Abm.PD.Core.Repository.DependencyInjection.ServiceCollectionExtension.AddCoreRepositoryServices` and `Abm.PD.Core.Repository.ProviderDirectoryDbContext` (Task 2); the `resource` table from Task 3.
- Produces: `Abm.PD.Core.Api.Settings.DatabaseSettings` — `public const string SectionName = "Database"`, `bool RunMigrationsOnStartup { get; init; }`. Produces the running host and its `GET /resources` endpoint, consumed only by manual verification in Task 5.

- [ ] **Step 1: Scaffold the minimal API project**

Run:
```bash
dotnet new web -n Abm.PD.Core.Api -o src/Abm.PD/Abm.PD.Core.Api
```
Expected: `Abm.PD.Core.Api.csproj` uses `Sdk="Microsoft.NET.Sdk.Web"`, `<TargetFramework>net10.0</TargetFramework>`. A minimal `Program.cs` (a single `"Hello World!"` route), `appsettings.json`, `appsettings.Development.json`, and `Properties/launchSettings.json` are created. Fix `TargetFramework` by hand if it isn't `net10.0`.

- [ ] **Step 2: Set TreatWarningsAsErrors and initialise user secrets**

Edit `Abm.PD.Core.Api.csproj`: add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` inside the existing `<PropertyGroup>`.

Run: `dotnet user-secrets init --project src/Abm.PD/Abm.PD.Core.Api/Abm.PD.Core.Api.csproj`
Expected: a `<UserSecretsId>` GUID is added to the `.csproj`, and output confirms `Set UserSecretsId to '...'`.

- [ ] **Step 3: Add package references and project references**

Run:
```bash
dotnet add src/Abm.PD/Abm.PD.Core.Api package Serilog.AspNetCore
dotnet add src/Abm.PD/Abm.PD.Core.Api package Microsoft.Extensions.Options.DataAnnotations
dotnet add src/Abm.PD/Abm.PD.Core.Api reference src/Abm.PD/Abm.PD.Core.Domain
dotnet add src/Abm.PD/Abm.PD.Core.Api reference src/Abm.PD/Abm.PD.Core.Repository
```

- [ ] **Step 4: Register the project in the solution**

Add to `src/Abm.PD/Abm.PD.slnx`:
```xml
<Project Path="Abm.PD.Core.Api/Abm.PD.Core.Api.csproj" />
```

- [ ] **Step 5: Write the DatabaseSettings options class**

Create `src/Abm.PD/Abm.PD.Core.Api/Settings/DatabaseSettings.cs`:
```csharp
namespace Abm.PD.Core.Api.Settings;

public record DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>
    /// Applies pending EF Core migrations on host startup. Intended for local development only —
    /// keep this false in production so schema changes are a deliberate, reviewed step rather
    /// than something that happens implicitly whenever the service restarts.
    /// </summary>
    public bool RunMigrationsOnStartup { get; init; }
}
```

- [ ] **Step 6: Replace appsettings.json**

Overwrite `src/Abm.PD/Abm.PD.Core.Api/appsettings.json`:
```json
{
  "Database": {
    "RunMigrationsOnStartup": false
  },
  "ConnectionStrings": {
    "ProviderDirectoryDb": ""
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.AspNetCore": "Warning",
        "System": "Error"
      }
    },
    "WriteTo": [
      {
        "Name": "Console"
      },
      {
        "Name": "File",
        "Args": {
          "path": "application.log",
          "rollingInterval": "Day"
        }
      }
    ]
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 7: Replace appsettings.Development.json**

Overwrite `src/Abm.PD/Abm.PD.Core.Api/appsettings.Development.json`:
```json
{
  "Database": {
    "RunMigrationsOnStartup": true
  }
}
```

- [ ] **Step 8: Replace Program.cs**

Overwrite `src/Abm.PD/Abm.PD.Core.Api/Program.cs`:
```csharp
using Abm.PD.Core.Api.Settings;
using Abm.PD.Core.Domain.Repositories;
using Abm.PD.Core.Repository;
using Abm.PD.Core.Repository.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging, configured entirely from the Serilog config section.
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddOptions<DatabaseSettings>()
    .Bind(builder.Configuration.GetSection(DatabaseSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddCoreRepositoryServices(builder.Configuration);

var app = builder.Build();

DatabaseSettings databaseSettings = app.Services.GetRequiredService<IOptions<DatabaseSettings>>().Value;

if (databaseSettings.RunMigrationsOnStartup)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    ProviderDirectoryDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapGet("/resources", async (IResourceRepository resourceRepository, CancellationToken cancellationToken) =>
    await resourceRepository.GetAllAsync(cancellationToken));

app.Run();
```

- [ ] **Step 9: Build and verify**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: Build succeeds, all seven projects listed (`Abm.PD.BulkExport`, `Abm.PD.Console`, `Abm.PD.Core.Api`, `Abm.PD.Core.Domain`, `Abm.PD.Core.Repository`, `Abm.PD.Tests`, and the test project), no warnings/errors.

- [ ] **Step 10: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Api src/Abm.PD/Abm.PD.slnx
git commit -m "Add Abm.PD.Core.Api with migration-on-startup toggle and GET /resources

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HVaKXvHUdjst4jhTJVKyKg"
```

---

### Task 5: End-to-end verification against the real local database

**Files:** none — this task runs the stack built in Tasks 1–4 and confirms it behaves as designed. No production code changes.

**Interfaces:**
- Consumes: everything produced by Tasks 1–4.

- [ ] **Step 1: Set the real connection string in user secrets**

Run:
```bash
dotnet user-secrets set "ConnectionStrings:ProviderDirectoryDb" "Host=localhost;Port=5432;Database=provider-directory;Username=admin;Password=admin" --project src/Abm.PD/Abm.PD.Core.Api/Abm.PD.Core.Api.csproj
```
Expected: confirmation the secret was written (`dotnet user-secrets list --project src/Abm.PD/Abm.PD.Core.Api/Abm.PD.Core.Api.csproj` shows the key, with the value visible only locally — it is never written to a tracked file).

- [ ] **Step 2: Run the Api on a fixed URL, in the background**

Run (background):
```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Abm.PD/Abm.PD.Core.Api --urls http://localhost:5299
```
Expected: Serilog console output shows the host starting, `Now listening on: http://localhost:5299`, and no unhandled exception (a migration failure or bad connection string would crash startup here — see the spec's Error Handling section).

- [ ] **Step 3: Call the endpoint**

Run: `curl -s http://localhost:5299/resources`
Expected: `[]` (HTTP 200, empty JSON array) — the `resource` table exists (created in Task 3) and is empty.

- [ ] **Step 4: Stop the background Api process**

Stop the process started in Step 2 (e.g. kill the background job/process).

- [ ] **Step 5: Full solution build, one last time**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: succeeds, all seven projects present, `Abm.PD.Core.Api` built with `TreatWarningsAsErrors` and no warnings.

- [ ] **Step 6: Confirm nothing was accidentally staged**

Run: `git status`
Expected: clean (Tasks 1–4 already committed their changes); no `appsettings*.json` diff containing the real connection string (it only ever went into user secrets, outside the repo).

No commit for this task — it's verification only, and every file it touches (user secrets) lives outside the repository.
