# Task Lookup Tables + ExportLoaderTask Repository Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Map `TaskStateId`/`TaskTypeId` as seeded EF Core lookup tables, fix `TaskBase`'s
mistakenly-BCL-typed status property, map the `TaskBase`/`ExportLoaderTask` hierarchy as
Table-Per-Concrete-Type, and add a repository that handles CRUS of `ExportLoaderTask` together with
its owned `ExportParameter` child (including its `TypeFilterList` as a native Postgres array).

**Architecture:** Three incremental tasks, each ending in a real EF Core migration run against the
project's `DesignTimeDbContextFactory` and a Testcontainers-backed Postgres integration test (no
mocked `DbContext` — see spec's Testing section for why). Task 1 lands the two lookup tables and the
`TaskStateId` rename. Task 2 lands the TPC mapping and the owned `ExportParameter`/`text[]` mapping,
proven with a direct `DbContext` round-trip test. Task 3 lands the public repository interface and its
full CRUS integration tests.

**Tech Stack:** .NET 10, EF Core 10 (`Microsoft.EntityFrameworkCore` 10.0.12), Npgsql provider 10.0.3,
`EFCore.NamingConventions` 10.0.1, xunit, Testcontainers.PostgreSql, Respawn.

**Spec:** `docs/superpowers/specs/2026-09-12-task-lookup-and-export-loader-task-design.md`

## Global Constraints

- Target framework `net10.0` on every touched project; `ImplicitUsings`/`Nullable` already enabled.
- No FK constraints between `export_loader_task` and `task_state`/`task_type` — plain int-shaped
  columns only, matching PyroServer's `HttpVerb` convention (spec: "Reference pattern").
- `TaskStatusId` is renamed to `TaskStateId` everywhere (enum members unchanged:
  `Ready`/`InProgress`/`OnHold`/`Completed`/`Failed`); `TaskBase.Status`/`StatusReason` become
  `State`/`StateReason`. Do not reintroduce a type named `TaskStatus` anywhere — it collides with
  `System.Threading.Tasks.TaskStatus` via this repo's `global using System.Threading.Tasks;` implicit
  using (spec: "Naming").
- `TaskBase`/`ExportLoaderTask` use EF Core's TPC mapping strategy (`UseTpcMappingStrategy()`) — no
  shared `task` table. `TaskBase.TypeId` is never mapped as a column (`Ignore(x => x.TypeId)`).
- `ExportParameter` is an EF owned entity on `ExportLoaderTask.Parameter` — no `Id` property, no
  independent table, folded into `export_loader_task`.
- `ExportParameter.TypeFilterList` maps to a native Postgres `text[]` column — no JSON, no value
  converter.
- `IExportLoaderTaskRepository.AddAsync`/`UpdateAsync` take the whole `ExportLoaderTask` object
  (including `Parameter`), not flat scalar parameters — this is a deliberate departure from
  `IProviderDataSourceRepository`'s convention, per the spec.
- `Abm.PD.Core.Api.Tests/Fixtures/IntegrationTestFixture.cs`'s `Respawner` must ignore `task_state`
  and `task_type` (seeded reference data) in addition to `__ef_migrations_history`.
- Every EF migration in this plan is generated with the real `dotnet ef` CLI against
  `DesignTimeDbContextFactory` — never hand-write migration `.cs`/`.Designer.cs` files.
- Australian English in comments; file-scoped namespaces; explicit types over `var`; multi-line
  method signatures with one parameter per line (per `CLAUDE.md`).

---

### Task 1: `TaskStateId` rename + seeded `TaskState`/`TaskType` lookup tables

**Files:**
- Delete: `src/Abm.PD/Abm.PD.Core.Domain/Enums/TaskStatusId.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Domain/Enums/TaskStateId.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskBase.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskState.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskType.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskStateConfiguration.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskTypeConfiguration.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/IntegrationTestFixture.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/<timestamp>_AddTaskStateAndTaskType.cs` (generated)
- Test: `src/Abm.PD/Abm.PD.Core.Api.Tests/Tasks/TaskLookupSeedTests.cs`

**Interfaces:**
- Produces: `Abm.PD.Core.Domain.Enums.TaskStateId` (enum: `Ready = 1, InProgress = 2, OnHold = 3, Completed = 4, Failed = 5`).
- Produces: `Abm.PD.Core.Domain.Entities.TaskState` — `TaskStateId TaskStateId { get; set; }`, `string Name { get; set; }`, public ctor `TaskState(TaskStateId taskStateId, string name)`.
- Produces: `Abm.PD.Core.Domain.Entities.TaskType` — `TaskTypeId TaskTypeId { get; set; }`, `string Name { get; set; }`, public ctor `TaskType(TaskTypeId taskTypeId, string name)`.
- Produces: `TaskBase.State` (was `Status`, now typed `TaskStateId`) and `TaskBase.StateReason` (was `StatusReason`) — Task 2 and Task 3 depend on these exact names.
- Produces: `ProviderDirectoryDbContext.TaskStates` / `.TaskTypes` (`DbSet<TaskState>` / `DbSet<TaskType>`).
- Produces: `IntegrationTestFixture.Services` (`IServiceProvider`) — Task 2 and Task 3's tests depend on this to resolve `ProviderDirectoryDbContext`/`IExportLoaderTaskRepository` without an HTTP API.

- [ ] **Step 1: Write the failing test**

Create `src/Abm.PD/Abm.PD.Core.Api.Tests/Tasks/TaskLookupSeedTests.cs`:

```csharp
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.Tasks;

public class TaskLookupSeedTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TaskStateTable_AfterMigration_ContainsOneRowPerEnumMember()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        ProviderDirectoryDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();

        List<TaskState> rows = await dbContext.TaskStates.AsNoTracking().ToListAsync();

        Assert.Equal(Enum.GetValues<TaskStateId>().Length, rows.Count);
        foreach (TaskStateId taskStateId in Enum.GetValues<TaskStateId>())
        {
            Assert.Contains(rows, x => x.TaskStateId == taskStateId && x.Name == taskStateId.ToString());
        }
    }

    [Fact]
    public async Task TaskTypeTable_AfterMigration_ContainsOneRowPerEnumMember()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        ProviderDirectoryDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();

        List<TaskType> rows = await dbContext.TaskTypes.AsNoTracking().ToListAsync();

        Assert.Equal(Enum.GetValues<TaskTypeId>().Length, rows.Count);
        foreach (TaskTypeId taskTypeId in Enum.GetValues<TaskTypeId>())
        {
            Assert.Contains(rows, x => x.TaskTypeId == taskTypeId && x.Name == taskTypeId.ToString());
        }
    }

    [Fact]
    public async Task TaskStateTable_AfterDatabaseReset_SeedDataStillPresent()
    {
        // ResetDatabaseAsync runs before every test via IntegrationTestBase.InitializeAsync - this
        // proves the Respawner ignore-list change (see IntegrationTestFixture) actually protects the
        // seeded lookup rows rather than wiping them.
        using IServiceScope scope = fixture.Services.CreateScope();
        ProviderDirectoryDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();

        int count = await dbContext.TaskStates.AsNoTracking().CountAsync();

        Assert.Equal(Enum.GetValues<TaskStateId>().Length, count);
    }
}
```

This will not compile yet: `TaskStateId`, `TaskState`, `TaskType`, `ProviderDirectoryDbContext.TaskStates`/`.TaskTypes`, and `IntegrationTestFixture.Services` don't exist.

- [ ] **Step 2: Run the test project to confirm it fails to compile**

Run: `dotnet build src/Abm.PD/Abm.PD.Core.Api.Tests`
Expected: build FAILS with errors like `CS0246: The type or namespace name 'TaskStateId' could not be found` and `CS1061: 'IntegrationTestFixture' does not contain a definition for 'Services'`.

- [ ] **Step 3: Rename the enum**

Run: `git rm src/Abm.PD/Abm.PD.Core.Domain/Enums/TaskStatusId.cs`

Then create `src/Abm.PD/Abm.PD.Core.Domain/Enums/TaskStateId.cs`:

```csharp
namespace Abm.PD.Core.Domain.Enums;

public enum TaskStateId
{
    Ready = 1,
    InProgress = 2,
    OnHold = 3,
    Completed = 4,
    Failed = 5
}
```

- [ ] **Step 4: Fix `TaskBase` — rename `Status`/`StatusReason` to `State`/`StateReason`, drop the BCL alias**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskBase.cs`:

```csharp
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Entities;

/// <summary>
/// A job definition is an entity that describes one particular type of executable work that could be run as a Job  
/// </summary>
public abstract class TaskBase
{
    public int Id { get; set; }

    public abstract TaskTypeId TypeId { get; }

    public abstract required string Code { get; set; }

    public abstract required string DisplayName { get; set; }

    public abstract string? Description { get; set; }

    public required TaskStateId State { get; set; }

    public required string? StateReason { get; set; }

    public required TimeSpan TriggerEvery { get; set; }

    public required DateTime? ToStartAtUtc { get; set; }

    public required DateTime? ToEndAtUtc { get; set; }

    public required DateTime CreatedUtc { get; set; }

    public required DateTime UpdatedUtc { get; set; }

    public required DateTime? LastStart { get; set; }

    public required DateTime? LastEnd { get; set; }
}
```

(The `using TaskStatus = System.Threading.Tasks.TaskStatus;` alias is gone — nothing in this file is
named `TaskStatus`/`TaskState` any more, so there is nothing left to alias around.)

- [ ] **Step 5: Create the `TaskState` lookup entity**

Create `src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskState.cs`:

```csharp
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Entities;

/// <summary>
/// Lookup/reference row for a TaskStateId enum member. Seeded once per enum value; not
/// referentially enforced against consuming columns - see TaskBase.State.
/// </summary>
public class TaskState
{
    private TaskState()
    {
    }

    public TaskState(TaskStateId taskStateId, string name)
    {
        TaskStateId = taskStateId;
        Name = name;
    }

    public TaskStateId TaskStateId { get; set; }

    public string Name { get; set; } = null!;
}
```

- [ ] **Step 6: Create the `TaskType` lookup entity**

Create `src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskType.cs`:

```csharp
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Entities;

/// <summary>
/// Lookup/reference row for a TaskTypeId enum member. Seeded once per enum value; not
/// referentially enforced against consuming columns.
/// </summary>
public class TaskType
{
    private TaskType()
    {
    }

    public TaskType(TaskTypeId taskTypeId, string name)
    {
        TaskTypeId = taskTypeId;
        Name = name;
    }

    public TaskTypeId TaskTypeId { get; set; }

    public string Name { get; set; } = null!;
}
```

- [ ] **Step 7: Configure and seed `TaskState`**

Create `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskStateConfiguration.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class TaskStateConfiguration : IEntityTypeConfiguration<TaskState>
{
    public void Configure(EntityTypeBuilder<TaskState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("task_state");
        builder.HasKey(x => x.TaskStateId);
        builder.Property(x => x.TaskStateId).HasConversion<int>();
        builder.Property(x => x.Name)
            .HasMaxLength(EntityConfigurationConstants.CodeMaxLength);

        builder.HasData(
            Enum.GetValues(typeof(TaskStateId))
                .Cast<TaskStateId>()
                .Select(e => new TaskState(e, e.ToString())));
    }
}
```

- [ ] **Step 8: Configure and seed `TaskType`**

Create `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskTypeConfiguration.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class TaskTypeConfiguration : IEntityTypeConfiguration<TaskType>
{
    public void Configure(EntityTypeBuilder<TaskType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("task_type");
        builder.HasKey(x => x.TaskTypeId);
        builder.Property(x => x.TaskTypeId).HasConversion<int>();
        builder.Property(x => x.Name)
            .HasMaxLength(EntityConfigurationConstants.CodeMaxLength);

        builder.HasData(
            Enum.GetValues(typeof(TaskTypeId))
                .Cast<TaskTypeId>()
                .Select(e => new TaskType(e, e.ToString())));
    }
}
```

- [ ] **Step 9: Add the two new `DbSet`s**

In `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`, add below the existing `DbSet`s:

```csharp
    public DbSet<TaskState> TaskStates => Set<TaskState>();
    public DbSet<TaskType> TaskTypes => Set<TaskType>();
```

(`OnModelCreating`'s `modelBuilder.ApplyConfigurationsFromAssembly(...)` call already picks up the two
new `IEntityTypeConfiguration` classes — no change needed there.)

- [ ] **Step 10: Expose `Services` on the fixture and protect seeded tables from `Respawner`**

In `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/IntegrationTestFixture.cs`:

Add a property next to `HttpClient`:

```csharp
    public IServiceProvider Services => _factory!.Services;
```

Update the `TablesToIgnore` list inside `InitializeAsync`:

```csharp
                TablesToIgnore =
                [
                    new Respawn.Graph.Table("__ef_migrations_history"),
                    // Seeded reference/lookup data (see TaskStateConfiguration/TaskTypeConfiguration's
                    // HasData) - not test data, must survive a reset like the migrations history table.
                    new Respawn.Graph.Table("task_state"),
                    new Respawn.Graph.Table("task_type"),
                ],
```

- [ ] **Step 11: Generate the migration**

Run from `src/Abm.PD/`:

```bash
dotnet ef migrations add AddTaskStateAndTaskType --project Abm.PD.Core.Repository --startup-project Abm.PD.Core.Api
```

If `dotnet ef` is not found, install it first: `dotnet tool install --global dotnet-ef --version 10.0.*`.

Inspect the generated migration under `src/Abm.PD/Abm.PD.Core.Repository/Migrations/` and confirm it
creates `task_state` and `task_type` tables (PK on `task_state_id`/`task_type_id`, `int`) with
`InsertData` calls seeding 5 and 1 row(s) respectively. Do not hand-edit the generated file.

- [ ] **Step 12: Build, then run the test to confirm it passes**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: builds cleanly.

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests --filter "FullyQualifiedName~TaskLookupSeedTests"`
Expected: all 3 tests PASS. (Requires Docker running locally for the Testcontainers Postgres instance.)

- [ ] **Step 13: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Domain/Enums/TaskStateId.cs src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskBase.cs src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskState.cs src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskType.cs src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskStateConfiguration.cs src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskTypeConfiguration.cs src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs src/Abm.PD/Abm.PD.Core.Repository/Migrations src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/IntegrationTestFixture.cs src/Abm.PD/Abm.PD.Core.Api.Tests/Tasks/TaskLookupSeedTests.cs
git commit -m "$(cat <<'EOF'
Rename TaskStatusId to TaskStateId and add seeded TaskState/TaskType lookup tables

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: TPC mapping for `ExportLoaderTask` + owned `ExportParameter` with `text[]` `TypeFilterList`

**Files:**
- Modify: `src/Abm.PD/Abm.PD.Core.Domain/Entities/ExportParameter.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskBaseConfiguration.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Configuration/ExportLoaderTaskConfiguration.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/<timestamp>_AddExportLoaderTask.cs` (generated)
- Test: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskMappingTests.cs`

**Interfaces:**
- Consumes: `TaskBase.State`/`StateReason` (Task 1), `IntegrationTestFixture.Services` (Task 1).
- Consumes: `ExportLoaderTask` (`Entities/ExportLoaderTask.cs`, already exists) — `TypeId` (computed,
  `TaskTypeId.BulkImport`), `Code`, `DisplayName`, `Description`, `Parameter` (`ExportParameter`,
  required).
- Produces: `ExportParameter` with no `Id` property (`Type`, `Since`, `TypeFilterList` only) — Task 3
  depends on this shape.
- Produces: `ProviderDirectoryDbContext.ExportLoaderTasks` (`DbSet<ExportLoaderTask>`) — Task 3 depends
  on this.

- [ ] **Step 1: Write the failing test**

Create `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskMappingTests.cs`:

```csharp
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportLoaderTaskMappingTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SaveAndReload_ExportLoaderTask_RoundTripsOwnedParameterAndTypeFilterListArray()
    {
        DateTime nowUtc = DateTime.UtcNow;
        ExportLoaderTask task = new()
        {
            Code = "bulk-import-au",
            DisplayName = "Bulk Import AU",
            Description = "Nightly bulk import",
            State = TaskStateId.Ready,
            StateReason = null,
            TriggerEvery = TimeSpan.FromHours(24),
            ToStartAtUtc = null,
            ToEndAtUtc = null,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = null,
            LastEnd = null,
            Parameter = new ExportParameter
            {
                Type = "Patient,Practitioner",
                Since = DateTimeOffset.UtcNow,
                TypeFilterList = ["Patient", "Practitioner"],
            },
        };

        using (IServiceScope writeScope = fixture.Services.CreateScope())
        {
            ProviderDirectoryDbContext writeContext =
                writeScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
            writeContext.ExportLoaderTasks.Add(task);
            await writeContext.SaveChangesAsync();
        }

        // A second, independent scope/DbContext forces a real read from Postgres rather than the
        // first-level change tracker cache.
        using IServiceScope readScope = fixture.Services.CreateScope();
        ProviderDirectoryDbContext readContext =
            readScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
        ExportLoaderTask reloaded = await readContext.ExportLoaderTasks
            .AsNoTracking()
            .SingleAsync(x => x.Code == "bulk-import-au");

        Assert.Equal(task.DisplayName, reloaded.DisplayName);
        Assert.Equal(TaskStateId.Ready, reloaded.State);
        Assert.Equal(TaskTypeId.BulkImport, reloaded.TypeId);
        Assert.Equal(new[] { "Patient", "Practitioner" }, reloaded.Parameter.TypeFilterList);
    }
}
```

This will not compile: `ExportParameter` still has an `Id` property that this test doesn't set (fine,
`Id` isn't `required`, so that alone wouldn't fail compilation) but `ProviderDirectoryDbContext` has no
`ExportLoaderTasks` member yet.

- [ ] **Step 2: Run the test project to confirm it fails to compile**

Run: `dotnet build src/Abm.PD/Abm.PD.Core.Api.Tests`
Expected: build FAILS — `CS1061: 'ProviderDirectoryDbContext' does not contain a definition for 'ExportLoaderTasks'`.

- [ ] **Step 3: Drop `ExportParameter.Id`**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Domain/Entities/ExportParameter.cs`:

```csharp
namespace Abm.PD.Core.Domain.Entities;

public class ExportParameter
{
    public required string Type { get; set; }

    public required DateTimeOffset? Since { get; set; }

    public required List<string> TypeFilterList { get; set; }
}
```

- [ ] **Step 4: Configure TPC on `TaskBase`**

Create `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskBaseConfiguration.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class TaskBaseConfiguration : IEntityTypeConfiguration<TaskBase>
{
    public void Configure(EntityTypeBuilder<TaskBase> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Table-Per-Concrete-Type: TaskBase has no table of its own. Each concrete task type
        // (ExportLoaderTask today) gets its own table carrying every TaskBase column plus its own -
        // see the design spec's TPC section for why (no cross-task-type querying is needed yet).
        builder.UseTpcMappingStrategy();
    }
}
```

- [ ] **Step 5: Configure `ExportLoaderTask` — table, ignored `TypeId`, owned `Parameter`**

Create `src/Abm.PD/Abm.PD.Core.Repository/Configuration/ExportLoaderTaskConfiguration.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class ExportLoaderTaskConfiguration : IEntityTypeConfiguration<ExportLoaderTask>
{
    public void Configure(EntityTypeBuilder<ExportLoaderTask> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("export_loader_task");

        // TypeId is a compile-time-constant computed property (see ExportLoaderTask.TypeId) - under
        // TPC the table itself already identifies the concrete type, so persisting it would just be
        // a constant column repeated on every row.
        builder.Ignore(x => x.TypeId);

        builder.OwnsOne(x => x.Parameter, parameter =>
        {
            parameter.Property(x => x.Type).HasColumnName("parameter_type");
            parameter.Property(x => x.Since).HasColumnName("parameter_since");
            parameter.Property(x => x.TypeFilterList).HasColumnName("parameter_type_filter_list");
        });
    }
}
```

- [ ] **Step 6: Add the `ExportLoaderTasks` `DbSet`**

In `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`, add:

```csharp
    public DbSet<ExportLoaderTask> ExportLoaderTasks => Set<ExportLoaderTask>();
```

- [ ] **Step 7: Generate the migration**

Run from `src/Abm.PD/`:

```bash
dotnet ef migrations add AddExportLoaderTask --project Abm.PD.Core.Repository --startup-project Abm.PD.Core.Api
```

Inspect the generated migration and confirm:
- It creates one table, `export_loader_task`, with columns for every `TaskBase` field
  (`id`, `code`, `display_name`, `description`, `state`, `state_reason`, `trigger_every`,
  `to_start_at_utc`, `to_end_at_utc`, `created_utc`, `updated_utc`, `last_start`, `last_end`) plus
  `parameter_type`, `parameter_since`, `parameter_type_filter_list`.
- No `type_id` column exists on `export_loader_task`.
- `parameter_type_filter_list`'s column type is `text[]` (an array), not `jsonb` or `text`. If EF/Npgsql
  did not map it to an array by convention, add explicit element configuration inside the `OwnsOne`
  block in Step 5:
  `parameter.PrimitiveCollection(x => x.TypeFilterList).HasColumnName("parameter_type_filter_list");`
  and regenerate the migration.

Do not hand-edit the generated file; if the shape is wrong, fix the configuration in Step 5 and
regenerate.

- [ ] **Step 8: Build, then run the test to confirm it passes**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: builds cleanly.

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests --filter "FullyQualifiedName~ExportLoaderTaskMappingTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Domain/Entities/ExportParameter.cs src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskBaseConfiguration.cs src/Abm.PD/Abm.PD.Core.Repository/Configuration/ExportLoaderTaskConfiguration.cs src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs src/Abm.PD/Abm.PD.Core.Repository/Migrations src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskMappingTests.cs
git commit -m "$(cat <<'EOF'
Map TaskBase/ExportLoaderTask as TPC with owned ExportParameter (text[] TypeFilterList)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: `IExportLoaderTaskRepository` + implementation, DI registration, full CRUS tests

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportLoaderTaskRepository.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/ExportLoaderTaskRepository.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs`
- Test: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs`

**Interfaces:**
- Consumes: `ExportLoaderTask`/`ExportParameter` (Task 2 shape, no `ExportParameter.Id`),
  `ProviderDirectoryDbContext.ExportLoaderTasks` (Task 2), `TaskStateId` (Task 1),
  `IntegrationTestFixture.Services` (Task 1).
- Produces: `IExportLoaderTaskRepository` with `GetAllAsync`, `GetByIdAsync(int, CancellationToken)`,
  `AddAsync(ExportLoaderTask, CancellationToken)`,
  `UpdateAsync(int, ExportLoaderTask, CancellationToken)`, `DeleteAsync(int, CancellationToken)`,
  `SearchAsync(string?, TaskStateId?, DateTime?, DateTime?, CancellationToken)` — this is the final
  public surface for this plan; nothing later depends on it.

- [ ] **Step 1: Write the failing tests**

Create `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs`:

```csharp
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportLoaderTaskRepositoryTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static ExportLoaderTask NewTask(
        string code,
        TaskStateId state = TaskStateId.Ready,
        DateTime? lastStart = null)
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new ExportLoaderTask
        {
            Code = code,
            DisplayName = $"Task {code}",
            Description = null,
            State = state,
            StateReason = null,
            TriggerEvery = TimeSpan.FromHours(24),
            ToStartAtUtc = null,
            ToEndAtUtc = null,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = lastStart,
            LastEnd = null,
            Parameter = new ExportParameter
            {
                Type = "Patient",
                Since = null,
                TypeFilterList = ["Patient"],
            },
        };
    }

    [Fact]
    public async Task AddAsync_NewTask_PersistsAndReturnsWithId()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();

        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.True(added.Id > 0);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingTask_ReturnsMatchingTaskWithParameter()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(added.Code, fetched!.Code);
        Assert.Equal(new[] { "Patient" }, fetched.Parameter.TypeFilterList);
    }

    [Fact]
    public async Task GetAllAsync_AfterAdd_ContainsAddedTask()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> all = await repository.GetAllAsync(CancellationToken.None);

        Assert.Contains(all, x => x.Id == added.Id);
    }

    [Fact]
    public async Task UpdateAsync_ExistingTask_PersistsChangesIncludingParameter()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        ExportLoaderTask update = NewTask(added.Code, TaskStateId.InProgress);
        update.Parameter.TypeFilterList = ["Patient", "Organization"];
        ExportLoaderTask? updated = await repository.UpdateAsync(added.Id, update, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(TaskStateId.InProgress, updated!.State);

        ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
        Assert.Equal(new[] { "Patient", "Organization" }, fetched.Parameter.TypeFilterList);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentTask_ReturnsNull()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();

        ExportLoaderTask? updated = await repository.UpdateAsync(999999, NewTask("missing"), CancellationToken.None);

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteAsync_ExistingTask_RemovesItThenGetByIdReturnsNull()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        bool deleted = await repository.DeleteAsync(added.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await repository.GetByIdAsync(added.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_ByCode_FindsMatchingTask()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        string code = Guid.NewGuid().ToString();
        await repository.AddAsync(NewTask(code), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> results = await repository.SearchAsync(
            code: code,
            state: null,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(code, results[0].Code);
    }

    [Fact]
    public async Task SearchAsync_ByState_FindsOnlyMatchingState()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        await repository.AddAsync(NewTask(Guid.NewGuid().ToString(), TaskStateId.Ready), CancellationToken.None);
        ExportLoaderTask inProgress = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), TaskStateId.InProgress), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> results = await repository.SearchAsync(
            code: null,
            state: TaskStateId.InProgress,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: CancellationToken.None);

        Assert.Contains(results, x => x.Id == inProgress.Id);
        Assert.All(results, x => Assert.Equal(TaskStateId.InProgress, x.State));
    }

    [Fact]
    public async Task SearchAsync_ByLastStartRange_FindsOnlyTasksWithinRange()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        DateTime inRange = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        DateTime outOfRange = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime rangeFrom = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime rangeTo = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        ExportLoaderTask matching = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), lastStart: inRange), CancellationToken.None);
        ExportLoaderTask nonMatching = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), lastStart: outOfRange), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> results = await repository.SearchAsync(
            code: null,
            state: null,
            lastStartFrom: rangeFrom,
            lastStartTo: rangeTo,
            cancellationToken: CancellationToken.None);

        Assert.Contains(results, x => x.Id == matching.Id);
        Assert.DoesNotContain(results, x => x.Id == nonMatching.Id);
        Assert.All(results, x => Assert.InRange(x.LastStart!.Value, rangeFrom, rangeTo));
    }
}
```

This will not compile: `IExportLoaderTaskRepository` doesn't exist yet.

- [ ] **Step 2: Run the test project to confirm it fails to compile**

Run: `dotnet build src/Abm.PD/Abm.PD.Core.Api.Tests`
Expected: build FAILS — `CS0246: The type or namespace name 'IExportLoaderTaskRepository' could not be found`.

- [ ] **Step 3: Create the repository interface**

Create `src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportLoaderTaskRepository.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Repositories;

public interface IExportLoaderTaskRepository
{
    Task<IReadOnlyList<ExportLoaderTask>> GetAllAsync(CancellationToken cancellationToken);

    Task<ExportLoaderTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<ExportLoaderTask> AddAsync(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken);

    Task<ExportLoaderTask?> UpdateAsync(
        int id,
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExportLoaderTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement the repository**

Create `src/Abm.PD/Abm.PD.Core.Repository/ExportLoaderTaskRepository.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository;

public class ExportLoaderTaskRepository(ProviderDirectoryDbContext dbContext) : IExportLoaderTaskRepository
{
    public async Task<IReadOnlyList<ExportLoaderTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ExportLoaderTasks
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<ExportLoaderTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.ExportLoaderTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<ExportLoaderTask> AddAsync(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        dbContext.ExportLoaderTasks.Add(exportLoaderTask);
        await dbContext.SaveChangesAsync(cancellationToken);
        return exportLoaderTask;
    }

    public async Task<ExportLoaderTask?> UpdateAsync(
        int id,
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        ExportLoaderTask? existing = await dbContext.ExportLoaderTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        existing.Code = exportLoaderTask.Code;
        existing.DisplayName = exportLoaderTask.DisplayName;
        existing.Description = exportLoaderTask.Description;
        existing.State = exportLoaderTask.State;
        existing.StateReason = exportLoaderTask.StateReason;
        existing.TriggerEvery = exportLoaderTask.TriggerEvery;
        existing.ToStartAtUtc = exportLoaderTask.ToStartAtUtc;
        existing.ToEndAtUtc = exportLoaderTask.ToEndAtUtc;
        existing.LastStart = exportLoaderTask.LastStart;
        existing.LastEnd = exportLoaderTask.LastEnd;
        // Mutate the tracked owned instance in place rather than replacing the reference - EF Core's
        // change tracking for owned types is more reliable against property mutation than reassignment.
        existing.Parameter.Type = exportLoaderTask.Parameter.Type;
        existing.Parameter.Since = exportLoaderTask.Parameter.Since;
        existing.Parameter.TypeFilterList = exportLoaderTask.Parameter.TypeFilterList;

        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        ExportLoaderTask? existing = await dbContext.ExportLoaderTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        dbContext.ExportLoaderTasks.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ExportLoaderTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        IQueryable<ExportLoaderTask> query = dbContext.ExportLoaderTasks.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(code))
        {
            query = query.Where(x => x.Code == code);
        }

        if (state is not null)
        {
            query = query.Where(x => x.State == state);
        }

        if (lastStartFrom is not null)
        {
            query = query.Where(x => x.LastStart != null && x.LastStart >= lastStartFrom);
        }

        if (lastStartTo is not null)
        {
            query = query.Where(x => x.LastStart != null && x.LastStart <= lastStartTo);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
```

- [ ] **Step 5: Register the repository in DI**

In `src/Abm.PD/Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs`, add below the
existing `services.AddScoped<IProviderDataSourceRepository, ProviderDataSourceRepository>();` line:

```csharp
        services.AddScoped<IExportLoaderTaskRepository, ExportLoaderTaskRepository>();
```

(Add `using Abm.PD.Core.Domain.Repositories;` is already present; `ExportLoaderTaskRepository` lives in
the same `Abm.PD.Core.Repository` namespace as this file, so no new `using` is needed for it.)

- [ ] **Step 6: Build, then run the tests to confirm they pass**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: builds cleanly.

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests --filter "FullyQualifiedName~ExportLoaderTaskRepositoryTests"`
Expected: all tests PASS.

Run the full test suite to confirm no regressions: `dotnet test src/Abm.PD/Abm.PD.slnx`
Expected: all tests PASS (including `ProviderDataSourceCrudTests`, `TaskLookupSeedTests`,
`ExportLoaderTaskMappingTests`).

- [ ] **Step 7: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportLoaderTaskRepository.cs src/Abm.PD/Abm.PD.Core.Repository/ExportLoaderTaskRepository.cs src/Abm.PD/Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs
git commit -m "$(cat <<'EOF'
Add IExportLoaderTaskRepository with CRUS for ExportLoaderTask and its owned Parameter

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```
