# Core.Api Integration Tests + Health Endpoints Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add production `/health/live` + `/health/ready` endpoints and a deployment `Dockerfile` to `Abm.PD.Core.Api`, and add a new `Abm.PD.Core.Api.Tests` project that exercises the full CRUD lifecycle of `/resources` plus both health endpoints end-to-end against a real, disposable Postgres container.

**Architecture:** The API is hosted in-process for tests via `WebApplicationFactory<Program>` (normal breakpoint debugging, no image-build cost) against a `Testcontainers.PostgreSql` container. `Respawn` resets the database to an empty checkpoint before every single test. This mirrors the proven pattern in the sibling `PyroServer` solution's `Abm.Pyro.Api.Test` project almost exactly, substituting Postgres for SQL Server. The Dockerfile is a real deployment artifact, unrelated to and unexercised by these tests.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core + Npgsql, xunit, Testcontainers.PostgreSql, Respawn, Microsoft.AspNetCore.Mvc.Testing, AspNetCore.HealthChecks.NpgSql.

**Spec:** `docs/superpowers/specs/2026-09-10-core-api-integration-tests-design.md`

## Global Constraints

- Target framework `net10.0`, `ImplicitUsings` and `Nullable` enabled on every project touched.
- `Abm.PD.Core.Api.csproj` has `TreatWarningsAsErrors` — a warning fails its build.
- Test project: xunit, no third-party mocking or assertion libraries (matches `Abm.PD.BulkExport.Tests`).
- Package versions (verified available on nuget.org at plan-writing time — use these exact versions):
  `Microsoft.AspNetCore.Mvc.Testing` 10.0.12, `Testcontainers.PostgreSql` 4.15.0, `Respawn` 7.0.0,
  `AspNetCore.HealthChecks.NpgSql` 9.0.0, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3,
  `xunit.runner.visualstudio` 2.8.2 (the last three matching `Abm.PD.BulkExport.Tests.csproj` exactly).
- File-scoped namespaces; primary constructors for DI; explicit types over `var`; comments explain
  *why*, not *what*; Australian English in comments/log messages.
- Every new/changed project must keep `dotnet build src/Abm.PD/Abm.PD.slnx` green.

---

## Task 1: Dockerfile for `Abm.PD.Core.Api`

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Api/Dockerfile`

**Interfaces:**
- Produces: a Docker image buildable from context `src/Abm.PD/`, entrypoint `dotnet Abm.PD.Core.Api.dll`, listening on 8080/8081. Not consumed by any later task — deployment artifact only, verified by `docker build` succeeding.

- [ ] **Step 1: Create the Dockerfile**

Mirrors `C:\GitRepo\angusmillar\PyroServer\src\Abm.Pyro.Api\Dockerfile`'s structure exactly, adjusted for this project's name and its two project references (`Abm.PD.Core.Domain`, `Abm.PD.Core.Repository`).

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

- [ ] **Step 2: Build the image to verify the Dockerfile is correct**

Run (from the repo root, so the build context is `src/Abm.PD/`):
```bash
docker build -t abm-pd-core-api-test -f src/Abm.PD/Abm.PD.Core.Api/Dockerfile src/Abm.PD
```
Expected: build completes successfully (`Successfully tagged abm-pd-core-api-test:latest` or the buildkit equivalent "naming to ... done").

- [ ] **Step 3: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Api/Dockerfile
git commit -m "$(cat <<'EOF'
Add Dockerfile for Abm.PD.Core.Api

Mirrors Abm.Pyro.Api/Dockerfile's multi-stage layout and chiseled base
images. Deployment artifact only - not exercised by the integration
tests added in this branch, which host the API in-process instead.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HVaKXvHUdjst4jhTJVKyKg
EOF
)"
```

---

## Task 2: `/health/live` and `/health/ready` endpoints

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Api/Endpoints/HealthCheckEndpoints.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Api/Program.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Api/Abm.PD.Core.Api.csproj`

**Interfaces:**
- Consumes: `builder.Configuration.GetConnectionString("ProviderDirectoryDb")` (already used by `AddCoreRepositoryServices` in `Abm.PD.Core.Repository.DependencyInjection.ServiceCollectionExtension`).
- Produces: `HealthCheckEndpoints.MapHealthCheckEndpoints(this IEndpointRouteBuilder)`, called from `Program.cs`. Routes `GET /health/live` (always 200, no DB check) and `GET /health/ready` (200 only if the `postgres` health check, tagged `ready`, passes). `public partial class Program { }` added to `Program.cs`, required by `WebApplicationFactory<Program>` in Task 4.

No automated test exists for these endpoints yet — `Abm.PD.Core.Api` has no unit test project of its own (see `CLAUDE.md`: only `Abm.PD.BulkExport.Tests` exists today). Automated coverage arrives in Task 4 (`HealthCheckTests`), once the integration test infrastructure exists to host the app. This task's verification is a successful build.

- [ ] **Step 1: Add the `AspNetCore.HealthChecks.NpgSql` package reference**

Edit `src/Abm.PD/Abm.PD.Core.Api/Abm.PD.Core.Api.csproj` — add to the existing `<ItemGroup>` of package references:

```xml
    <PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="9.0.0" />
```

so the full package `<ItemGroup>` reads:

```xml
  <ItemGroup>
    <PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="9.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" />
    <PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
  </ItemGroup>
```

- [ ] **Step 2: Create `HealthCheckEndpoints.cs`**

```csharp
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Abm.PD.Core.Api.Endpoints;

public static class HealthCheckEndpoints
{
    public static IEndpointRouteBuilder MapHealthCheckEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness checks nothing downstream, so a Postgres blip never fails a liveness probe and
        // causes an otherwise-healthy process to be killed and restarted.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponseAsync,
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteResponseAsync,
        });

        return endpoints;
    }

    private static Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    duration = entry.Value.Duration.TotalMilliseconds,
                }),
        };

        return context.Response.WriteAsJsonAsync(payload);
    }
}
```

- [ ] **Step 3: Wire health checks and the new endpoints into `Program.cs`**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Api/Program.cs` with:

```csharp
using Abm.PD.Core.Api.Endpoints;
using Abm.PD.Core.Api.Settings;
using Abm.PD.Core.Repository;
using Abm.PD.Core.Repository.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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

string connectionString = builder.Configuration.GetConnectionString("ProviderDirectoryDb")
    ?? throw new InvalidOperationException(
        "Missing required connection string 'ConnectionStrings:ProviderDirectoryDb'.");

builder.Services.AddCoreRepositoryServices(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

var app = builder.Build();

DatabaseSettings databaseSettings = app.Services.GetRequiredService<IOptions<DatabaseSettings>>().Value;

if (databaseSettings.RunMigrationsOnStartup)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    ProviderDirectoryDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapResourceEndpoints();
app.MapHealthCheckEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> in Abm.PD.Core.Api.Tests - top-level statements
// otherwise generate an internal Program class the test assembly cannot reference.
public partial class Program { }
```

- [ ] **Step 4: Build to verify it compiles clean (TreatWarningsAsErrors is on for this project)**

Run: `dotnet build src/Abm.PD/Abm.PD.Core.Api/Abm.PD.Core.Api.csproj`
Expected: `Build succeeded.` with no warnings or errors.

- [ ] **Step 5: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Api/Endpoints/HealthCheckEndpoints.cs src/Abm.PD/Abm.PD.Core.Api/Program.cs src/Abm.PD/Abm.PD.Core.Api/Abm.PD.Core.Api.csproj
git commit -m "$(cat <<'EOF'
Add /health/live and /health/ready endpoints to Abm.PD.Core.Api

Liveness checks nothing downstream (process-alive only); readiness
runs the Postgres check via AspNetCore.HealthChecks.NpgSql. Both
endpoints share one structured JSON ResponseWriter. Also adds the
public partial class Program marker WebApplicationFactory<Program>
needs in the integration test project added later on this branch.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HVaKXvHUdjst4jhTJVKyKg
EOF
)"
```

---

## Task 3: Scaffold `Abm.PD.Core.Api.Tests`

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Api.Tests/Abm.PD.Core.Api.Tests.csproj`
- Modify: `src/Abm.PD/Abm.PD.slnx`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj`

**Interfaces:**
- Produces: an empty-but-building `Abm.PD.Core.Api.Tests` project referencing `Abm.PD.Core.Api` (which transitively brings in `Abm.PD.Core.Domain` and `Abm.PD.Core.Repository`, including their `Microsoft.EntityFrameworkCore`/`Npgsql.EntityFrameworkCore.PostgreSQL` package references). `Abm.PD.Core.Repository`'s `internal` types (specifically `NpgsqlDbContextOptionsSupport`, used in Task 4) become visible to the new test assembly.

- [ ] **Step 1: Create the test project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <IsPackable>false</IsPackable>
        <IsTestProject>true</IsTestProject>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
        <PackageReference Include="xunit" Version="2.9.3" />
        <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
        <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
        <PackageReference Include="Testcontainers.PostgreSql" Version="4.15.0" />
        <PackageReference Include="Respawn" Version="7.0.0" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\Abm.PD.Core.Api\Abm.PD.Core.Api.csproj" />
    </ItemGroup>

</Project>
```

Save as `src/Abm.PD/Abm.PD.Core.Api.Tests/Abm.PD.Core.Api.Tests.csproj`.

- [ ] **Step 2: Add the project to the solution**

Edit `src/Abm.PD/Abm.PD.slnx`, inserting the new project entry alphabetically after `Abm.PD.Core.Api`:

```xml
<Solution>
  <Project Path="Abm.PD.BulkExport.Tests/Abm.PD.BulkExport.Tests.csproj" />
  <Project Path="Abm.PD.BulkExport/Abm.PD.BulkExport.csproj" />
  <Project Path="Abm.PD.Console/Abm.PD.Console.csproj" />
  <Project Path="Abm.PD.Core.Api/Abm.PD.Core.Api.csproj" />
  <Project Path="Abm.PD.Core.Api.Tests/Abm.PD.Core.Api.Tests.csproj" />
  <Project Path="Abm.PD.Core.Domain/Abm.PD.Core.Domain.csproj" />
  <Project Path="Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj" />
</Solution>
```

- [ ] **Step 3: Allow the test project to see `Abm.PD.Core.Repository`'s internal members**

Edit `src/Abm.PD/Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj`, adding a new `<ItemGroup>`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Abm.PD.Core.Api.Tests" />
  </ItemGroup>
```

Full file becomes:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="EFCore.NamingConventions" Version="10.0.1" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.12" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Abm.PD.Core.Api.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Abm.PD.Core.Domain\Abm.PD.Core.Domain.csproj" />
  </ItemGroup>

</Project>
```

The only change from the existing file is the new `InternalsVisibleTo` `<ItemGroup>` — the existing `<ItemGroup>` of `<PackageReference>` entries and the `<ProjectReference>` are unchanged.

- [ ] **Step 4: Build the whole solution to verify the new project is wired in correctly**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: `Build succeeded.`, all eight projects listed in the output.

- [ ] **Step 5: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Api.Tests/Abm.PD.Core.Api.Tests.csproj src/Abm.PD/Abm.PD.slnx src/Abm.PD/Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj
git commit -m "$(cat <<'EOF'
Scaffold Abm.PD.Core.Api.Tests project

Empty xunit project referencing Abm.PD.Core.Api, added to
Abm.PD.slnx. Abm.PD.Core.Repository grants it InternalsVisibleTo so
the fixture built in the next commit can reuse
NpgsqlDbContextOptionsSupport rather than duplicating its Npgsql/
snake-case/retry configuration.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HVaKXvHUdjst4jhTJVKyKg
EOF
)"
```

---

## Task 4: Test infrastructure + health check tests

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/IntegrationTestCollection.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/CoreApiWebApplicationFactory.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/IntegrationTestFixture.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/IntegrationTestBase.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Api.Tests/Health/HealthCheckTests.cs`

**Interfaces:**
- Consumes: `Program` (public partial class, from Task 2), `NpgsqlDbContextOptionsSupport.ConfigureProviderDirectoryDbContext(DbContextOptionsBuilder, string connectionString, bool enableRetryOnFailure)` (internal, `Abm.PD.Core.Repository`, now visible via `InternalsVisibleTo`), `ProviderDirectoryDbContext` (`Abm.PD.Core.Repository`).
- Produces: `IntegrationTestFixture` — `HttpClient HttpClient { get; }`, `Task InitializeAsync()`, `Task ResetDatabaseAsync()`, `Task DisposeAsync()`. `IntegrationTestBase(IntegrationTestFixture fixture)` — abstract, `protected HttpClient HttpClient`, resets the database in `InitializeAsync()` before every test. `IntegrationTestCollection` — the `[Collection(nameof(IntegrationTestCollection))]` name every test class (via its base) shares, so exactly one `IntegrationTestFixture` is created per test run. All of Task 5's tests derive from `IntegrationTestBase`.

Requires a running local Docker daemon (confirmed available: Docker 29.7.2).

- [ ] **Step 1: Create the collection marker**

```csharp
namespace Abm.PD.Core.Api.Tests.Fixtures;

[CollectionDefinition(nameof(IntegrationTestCollection))]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    // Marker class only. xUnit wires the fixture automatically.
}
```

Save as `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/IntegrationTestCollection.cs`.

- [ ] **Step 2: Create the custom `WebApplicationFactory`**

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Abm.PD.Core.Api.Tests.Fixtures;

public class CoreApiWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Point the app at the Testcontainers Postgres container instead of local dev Postgres.
                ["ConnectionStrings:ProviderDirectoryDb"] = connectionString,

                // IntegrationTestFixture migrates the container directly before this factory starts;
                // the app must not also try to migrate on every WebApplicationFactory boot.
                ["Database:RunMigrationsOnStartup"] = "false",

                // Quiet logging in tests.
                ["Serilog:MinimumLevel:Default"] = "Warning",
            });
        });
    }
}
```

Save as `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/CoreApiWebApplicationFactory.cs`.

- [ ] **Step 3: Create the fixture**

```csharp
using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Abm.PD.Core.Api.Tests.Fixtures;

public class IntegrationTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder().Build();

    private Respawner _respawner = default!;
    private CoreApiWebApplicationFactory _factory = default!;
    private string _connectionString = default!;

    public HttpClient HttpClient { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        // 1. Start the Postgres container.
        await _postgreSqlContainer.StartAsync();
        _connectionString = _postgreSqlContainer.GetConnectionString();

        // 2. Apply EF Core migrations against it, reusing the same Npgsql/snake-case/retry
        //    configuration as every other call site (see NpgsqlDbContextOptionsSupport's own
        //    doc comment) so this fixture cannot silently drift from production configuration.
        DbContextOptionsBuilder<ProviderDirectoryDbContext> optionsBuilder = new();
        NpgsqlDbContextOptionsSupport.ConfigureProviderDirectoryDbContext(
            optionsBuilder: optionsBuilder,
            connectionString: _connectionString,
            enableRetryOnFailure: false);
        await using (ProviderDirectoryDbContext context = new(optionsBuilder.Options))
        {
            await context.Database.MigrateAsync();
        }

        // 3. Checkpoint the migrated, empty database - only the resource table exists today; add
        //    more tables here as the schema grows.
        await using (NpgsqlConnection checkpointConnection = new(_connectionString))
        {
            await checkpointConnection.OpenAsync();
            _respawner = await Respawner.CreateAsync(checkpointConnection, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToInclude = [new Respawn.Graph.Table("resource")],
            });
        }

        // 4. Start the API in-process against the container.
        _factory = new CoreApiWebApplicationFactory(_connectionString);
        HttpClient = _factory.CreateClient();
    }

    public async Task ResetDatabaseAsync()
    {
        await using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        HttpClient.Dispose();
        await _factory.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
    }
}
```

Save as `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/IntegrationTestFixture.cs`.

- [ ] **Step 4: Create the shared test base class**

```csharp
namespace Abm.PD.Core.Api.Tests.Fixtures;

[Collection(nameof(IntegrationTestCollection))]
public abstract class IntegrationTestBase(IntegrationTestFixture fixture) : IAsyncLifetime
{
    protected HttpClient HttpClient => fixture.HttpClient;

    // Runs before every [Fact] - xUnit creates a fresh instance of the derived test class per test
    // method, so every test starts from a genuinely empty database.
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
```

Save as `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/IntegrationTestBase.cs`.

- [ ] **Step 5: Write the health check tests**

```csharp
using System.Net;
using Abm.PD.Core.Api.Tests.Fixtures;

namespace Abm.PD.Core.Api.Tests.Health;

public class HealthCheckTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Live_ReturnsOk()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_WithDatabaseReachable_ReturnsOk()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

Save as `src/Abm.PD/Abm.PD.Core.Api.Tests/Health/HealthCheckTests.cs`.

- [ ] **Step 6: Run the tests to prove the whole pipeline — container start, migration, Respawn checkpoint, in-process API boot, real HTTP round-trip — actually works**

Ensure Docker Desktop is running, then:

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests/Abm.PD.Core.Api.Tests.csproj`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`. If it fails, check Docker is running before debugging further — a Testcontainers connection failure is the most likely first-run cause, not application code.

- [ ] **Step 7: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/ src/Abm.PD/Abm.PD.Core.Api.Tests/Health/
git commit -m "$(cat <<'EOF'
Add Testcontainers/WebApplicationFactory/Respawn test infrastructure

IntegrationTestFixture starts one Postgres container per test run,
migrates it directly, and hosts the API in-process via
CoreApiWebApplicationFactory. IntegrationTestBase resets the database
with Respawn before every test. Mirrors Abm.Pyro.Api.Test's fixture
shape from the PyroServer solution. HealthCheckTests is the first
proof the whole pipeline works end-to-end.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HVaKXvHUdjst4jhTJVKyKg
EOF
)"
```

---

## Task 5: Resource CRUD integration tests

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Api.Tests/Resources/ResourceCrudTests.cs`

**Interfaces:**
- Consumes: `IntegrationTestBase` and `HttpClient` (Task 4), `ResourceRequest(string ResourceType, string ResourceId)` (`Abm.PD.Core.Api.Contracts`), `Resource { int Id; string ResourceType; string ResourceId; }` (`Abm.PD.Core.Domain.Entities`), and the live `/resources` endpoints in `Abm.PD.Core.Api.Endpoints.ResourceEndpoints` (already implemented, unchanged by this plan).

These tests characterise already-implemented, already-working endpoint behaviour (`ResourceEndpoints`/`ResourceRepository` predate this plan) rather than driving new production code, so there is no red/implement/green cycle here — just write the tests against the real pipeline proven working in Task 4, and confirm they pass.

- [ ] **Step 1: Write the CRUD lifecycle tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Api.Tests.Resources;

public class ResourceCrudTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Create_ValidRequest_Returns201WithCreatedResource()
    {
        ResourceRequest request = new("Practitioner", Guid.NewGuid().ToString());

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/resources", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Resource? created = await response.Content.ReadFromJsonAsync<Resource>();
        Assert.NotNull(created);
        Assert.Equal(request.ResourceType, created!.ResourceType);
        Assert.Equal(request.ResourceId, created.ResourceId);
    }

    [Fact]
    public async Task GetById_ExistingResource_ReturnsMatchingResource()
    {
        ResourceRequest request = new("Organization", Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/resources", request);
        Resource created = (await createResponse.Content.ReadFromJsonAsync<Resource>())!;

        Resource? fetched = await HttpClient.GetFromJsonAsync<Resource>($"/resources/{created.Id}");

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(request.ResourceType, fetched.ResourceType);
    }

    [Fact]
    public async Task GetAll_AfterCreate_ContainsCreatedResource()
    {
        ResourceRequest request = new("Location", Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/resources", request);
        Resource created = (await createResponse.Content.ReadFromJsonAsync<Resource>())!;

        List<Resource>? all = await HttpClient.GetFromJsonAsync<List<Resource>>("/resources");

        Assert.NotNull(all);
        Assert.Contains(all!, r => r.Id == created.Id);
    }

    [Fact]
    public async Task Search_ByResourceTypeAndId_FindsMatchingResource()
    {
        string resourceId = Guid.NewGuid().ToString();
        ResourceRequest request = new("Endpoint", resourceId);
        await HttpClient.PostAsJsonAsync("/resources", request);

        List<Resource>? results = await HttpClient.GetFromJsonAsync<List<Resource>>(
            $"/resources/search?resourceType=Endpoint&resourceId={resourceId}");

        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal(resourceId, results![0].ResourceId);
    }

    [Fact]
    public async Task Update_ExistingResource_PersistsChanges()
    {
        ResourceRequest request = new("HealthcareService", Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/resources", request);
        Resource created = (await createResponse.Content.ReadFromJsonAsync<Resource>())!;

        ResourceRequest updateRequest = new("HealthcareService", Guid.NewGuid().ToString());
        HttpResponseMessage updateResponse = await HttpClient.PutAsJsonAsync($"/resources/{created.Id}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Resource? updated = await updateResponse.Content.ReadFromJsonAsync<Resource>();
        Assert.Equal(updateRequest.ResourceId, updated!.ResourceId);
    }

    [Fact]
    public async Task Delete_ExistingResource_Returns204ThenGetByIdReturns404()
    {
        ResourceRequest request = new("PractitionerRole", Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/resources", request);
        Resource created = (await createResponse.Content.ReadFromJsonAsync<Resource>())!;

        HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"/resources/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"/resources/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentResource_Returns404()
    {
        ResourceRequest updateRequest = new("Practitioner", Guid.NewGuid().ToString());

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync("/resources/999999", updateRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

Save as `src/Abm.PD/Abm.PD.Core.Api.Tests/Resources/ResourceCrudTests.cs`.

- [ ] **Step 2: Run the full test project**

Ensure Docker Desktop is running, then:

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests/Abm.PD.Core.Api.Tests.csproj`
Expected: `Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9` (2 health tests from Task 4 + 7 CRUD tests here).

- [ ] **Step 3: Build the whole solution one final time**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: `Build succeeded.`, all eight projects.

- [ ] **Step 4: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Api.Tests/Resources/
git commit -m "$(cat <<'EOF'
Add end-to-end CRUD tests for the /resources endpoints

Create, get-by-id, get-all, search, update, delete, and the two 404
paths (get-after-delete, update-nonexistent), all driven over real
HTTP against the in-process API and a real Postgres container.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01HVaKXvHUdjst4jhTJVKyKg
EOF
)"
```

---

## Self-Review Notes

- **Spec coverage:** Dockerfile (Task 1) ✓, health endpoints (Task 2) ✓, test project scaffold + `Abm.PD.slnx` entry (Task 3) ✓, `IntegrationTestFixture`/`CoreApiWebApplicationFactory`/`IntegrationTestCollection`/`IntegrationTestBase` (Task 4) ✓, CRUD + health test coverage (Tasks 4-5) ✓, `public partial class Program` (Task 2) ✓.
- **Placeholder scan:** none — every step has complete, concrete code.
- **Type consistency:** `IntegrationTestFixture.HttpClient`/`ResetDatabaseAsync()` (Task 4) match their usage in `IntegrationTestBase` (Task 4) and every test class (Tasks 4-5). `CoreApiWebApplicationFactory(string connectionString)` constructor signature matches its call site in `IntegrationTestFixture`. `ResourceRequest`/`Resource` property names match `Abm.PD.Core.Api.Contracts.ResourceRequest` and `Abm.PD.Core.Domain.Entities.Resource` exactly as they exist in the codebase today.
