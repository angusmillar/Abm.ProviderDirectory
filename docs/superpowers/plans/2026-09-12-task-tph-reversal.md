# TaskBase/ExportLoaderTask TPC → TPH Reversal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Flip the `TaskBase`/`ExportLoaderTask` EF Core mapping from Table-Per-Concrete-Type to
Table-Per-Hierarchy: `TypeId` becomes a real stored discriminator column, `Code`/`DisplayName`/
`Description` become plain (non-overridden) `TaskBase` properties, `ExportParameter` moves into its
own table, and every migration is dropped and replaced with one fresh `InitialCreate`.

**Architecture:** This is one atomic mapping-strategy change — the entity shape, the EF configuration,
and the generated migration all have to move together or the project won't build/run, so it is one
task rather than several independently-shippable ones. The task proceeds: entities → EF configuration
→ delete the now-obsolete TPC-only convention and its tests → delete and regenerate migrations →
update the two existing integration tests that construct `ExportLoaderTask` → verify with a real
Testcontainers-backed `dotnet test` run.

**Tech Stack:** .NET 10, EF Core 10, Npgsql provider 10.0.3, `EFCore.NamingConventions` 10.0.1, xunit,
Testcontainers.PostgreSql, Respawn.

**Spec:** `docs/superpowers/specs/2026-09-12-task-tph-reversal-design.md`

## Global Constraints

- Target framework `net10.0`; `ImplicitUsings`/`Nullable` already enabled on every touched project.
- `TaskBase.TypeId` is `required TaskTypeId TypeId { get; set; }` — no longer abstract, no longer
  overridden in `ExportLoaderTask`. Every `new ExportLoaderTask { ... }` call site must now set
  `TypeId = TaskTypeId.BulkImport` explicitly (spec: "`TaskBase`: stored `TypeId`...").
- `Code`, `DisplayName`, `Description` live only on `TaskBase` — `ExportLoaderTask` must not redeclare
  or override them.
- One shared table `task` for the whole `TaskBase` hierarchy; discriminator is the real `TypeId`
  column via `HasDiscriminator(x => x.TypeId).HasValue<ExportLoaderTask>(TaskTypeId.BulkImport)`
  (spec: "EF mapping").
- `ExportParameter` remains an EF owned entity of `ExportLoaderTask.Parameter` but maps to its own
  table `export_loader_task_parameter`, columns `type`/`since`/`type_filter_list` (no `parameter_`
  prefix — spec: "EF mapping").
- `TpcOwnedEntityKeyNameFixupConvention.cs` and `TpcOwnedEntityKeyNameFixupConventionTests.cs` are
  deleted outright, along with the `ConfigureConventions` override in `ProviderDirectoryDbContext`
  that registered it.
- Every existing migration file and `ProviderDirectoryDbContextModelSnapshot.cs` is deleted; the
  replacement `InitialCreate` is generated with the real `dotnet ef` CLI against
  `DesignTimeDbContextFactory` — never hand-written (per `CLAUDE.md`'s migration convention).
- Australian English in comments; file-scoped namespaces; explicit types over `var`; multi-line method
  signatures with one parameter per line (per `CLAUDE.md`).
- No FK constraints introduced between `task` and `task_state`/`task_type` — unchanged from before.

---

### Task 1: Flip TaskBase/ExportLoaderTask to TPH, regenerate migrations, verify

**Files:**
- Modify: `src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskBase.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Domain/Entities/ExportLoaderTask.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskBaseConfiguration.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/Configuration/ExportLoaderTaskConfiguration.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TpcOwnedEntityKeyNameFixupConvention.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Api.Tests/Conventions/TpcOwnedEntityKeyNameFixupConventionTests.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260910093425_InitialCreate.cs` and `.Designer.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911070439_AddProviderDataSource.cs` and `.Designer.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911165634_AddTaskStateAndTaskType.cs` and `.Designer.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911171340_AddExportLoaderTask.cs` and `.Designer.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911174830_AddExportLoaderTaskCodeConstraint.cs` and `.Designer.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/ProviderDirectoryDbContextModelSnapshot.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/<timestamp>_InitialCreate.cs` and `.Designer.cs` (generated)
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/ProviderDirectoryDbContextModelSnapshot.cs` (generated)
- Modify: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskMappingTests.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs`

**Interfaces:**
- Consumes: `Abm.PD.Core.Domain.Enums.TaskTypeId` (existing: `BulkImport = 1`), `Abm.PD.Core.Domain.Enums.TaskStateId` (existing), `Abm.PD.Core.Domain.Entities.ExportParameter` (existing, unchanged).
- Produces: `TaskBase.TypeId` — `required TaskTypeId TypeId { get; set; }` (was abstract get-only). Nothing outside this task's own files reads or sets it today.
- Produces: `TaskBase.Code`/`DisplayName`/`Description` as plain (non-abstract) properties — same names and types as before, just no longer redeclared on `ExportLoaderTask`.

- [ ] **Step 1: Update `TaskBase.cs` to hold `TypeId` and the shared properties as plain members**

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

    public required TaskTypeId TypeId { get; set; }

    public required string Code { get; set; }

    public required string DisplayName { get; set; }

    public string? Description { get; set; }

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

- [ ] **Step 2: Shrink `ExportLoaderTask.cs` to just its own data**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Domain/Entities/ExportLoaderTask.cs`:

```csharp
namespace Abm.PD.Core.Domain.Entities;

public class ExportLoaderTask : TaskBase
{
    public required ExportParameter Parameter { get; set; }
}
```

- [ ] **Step 3: Try to build — confirm it fails where `TypeId`/`Code`/`DisplayName`/`Description` were overridden**

Run: `dotnet build src/Abm.PD/Abm.PD.Core.Domain`

Expected: FAIL — `ExportLoaderTaskConfiguration.cs` still calls `builder.Ignore(x => x.TypeId)` and
`Abm.PD.Core.Repository`'s `TaskBaseConfiguration.cs` still calls `builder.UseTpcMappingStrategy()`
against a model that no longer matches; the domain project itself builds fine (it has no more
`override` keywords to clash), but building the whole solution surfaces the config mismatch in the
next step. Treat this step as a checkpoint, not a hard gate — proceed to Step 4 regardless.

- [ ] **Step 4: Rewrite `TaskBaseConfiguration.cs` for TPH**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskBaseConfiguration.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class TaskBaseConfiguration : IEntityTypeConfiguration<TaskBase>
{
    public void Configure(EntityTypeBuilder<TaskBase> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Table-Per-Hierarchy: every TaskBase subtype (ExportLoaderTask today) shares this one
        // table, discriminated by the stored TypeId column - see the design spec's TPH section for
        // why this replaced the earlier TPC decision.
        builder.ToTable("task");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .HasMaxLength(EntityConfigurationConstants.CodeMaxLength);

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.HasDiscriminator(x => x.TypeId)
            .HasValue<ExportLoaderTask>(TaskTypeId.BulkImport);
    }
}
```

- [ ] **Step 5: Rewrite `ExportLoaderTaskConfiguration.cs` to map `Parameter` into its own table**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Repository/Configuration/ExportLoaderTaskConfiguration.cs`:

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

        builder.OwnsOne(x => x.Parameter, parameter =>
        {
            parameter.ToTable("export_loader_task_parameter");
            parameter.Property(x => x.Type).HasColumnName("type");
            parameter.Property(x => x.Since).HasColumnName("since");
            parameter.Property(x => x.TypeFilterList).HasColumnName("type_filter_list");
        });
    }
}
```

- [ ] **Step 6: Delete the now-obsolete TPC key-naming convention and its dedicated test class**

Delete `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TpcOwnedEntityKeyNameFixupConvention.cs` and
`src/Abm.PD/Abm.PD.Core.Api.Tests/Conventions/TpcOwnedEntityKeyNameFixupConventionTests.cs` (and the
`Conventions` folder if now empty).

- [ ] **Step 7: Remove the convention's registration from `ProviderDirectoryDbContext.cs`**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository;

public class ProviderDirectoryDbContext(DbContextOptions<ProviderDirectoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ProviderDataSource> ProviderDataSource => Set<ProviderDataSource>();
    public DbSet<TaskState> TaskStates => Set<TaskState>();
    public DbSet<TaskType> TaskTypes => Set<TaskType>();
    public DbSet<ExportLoaderTask> ExportLoaderTasks => Set<ExportLoaderTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProviderDirectoryDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
```

- [ ] **Step 8: Build the whole solution — confirm it now succeeds**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: succeeds with no warnings/errors (both `Abm.PD.Core.Api` and `Abm.PD.Core.Repository` treat
warnings as errors where configured — check `.csproj` `TreatWarningsAsErrors` settings if this fails).

- [ ] **Step 9: Delete every existing migration file and the model snapshot**

Delete these ten files plus the snapshot:
- `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260910093425_InitialCreate.cs`
- `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260910093425_InitialCreate.Designer.cs`
- `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911070439_AddProviderDataSource.cs`
- `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911070439_AddProviderDataSource.Designer.cs`
- `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911165634_AddTaskStateAndTaskType.cs`
- `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911165634_AddTaskStateAndTaskType.Designer.cs`
- `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911171340_AddExportLoaderTask.cs`
- `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911171340_AddExportLoaderTask.Designer.cs`
- `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911174830_AddExportLoaderTaskCodeConstraint.cs`
- `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260911174830_AddExportLoaderTaskCodeConstraint.Designer.cs`
- `src/Abm.PD/Abm.PD.Core.Repository/Migrations/ProviderDirectoryDbContextModelSnapshot.cs`

- [ ] **Step 10: Generate the fresh `InitialCreate` migration with the real `dotnet ef` CLI**

Run (from the repository root, per `CLAUDE.md`'s migration convention):

```powershell
dotnet ef migrations add InitialCreate --project src/Abm.PD/Abm.PD.Core.Repository --startup-project src/Abm.PD/Abm.PD.Core.Repository
```

Expected: creates a new `<timestamp>_InitialCreate.cs`/`.Designer.cs` pair and a fresh
`ProviderDirectoryDbContextModelSnapshot.cs` under
`src/Abm.PD/Abm.PD.Core.Repository/Migrations/`. Open the generated `.cs` file and confirm it contains
exactly one `CreateTable` for `"task"` (with a `type_id` integer column, no default sequence — just
`NpgsqlValueGenerationStrategy.IdentityByDefaultColumn` on `id`, matching `resource`/
`provider_data_source`'s pattern) and one `CreateTable` for `"export_loader_task_parameter"` (columns
`export_loader_task_id` (PK+FK), `type`, `since`, `type_filter_list`), alongside the unchanged
`resource`, `provider_data_source`, `task_state`, `task_type` tables and their `InsertData` seed calls.
If a `TaskBaseSequence` or any TPC-shaped artefact appears, the entity/config changes in Steps 1–5 did
not fully take — stop and re-check those files before continuing.

- [ ] **Step 11: Update `ExportLoaderTaskMappingTests.cs` to set the now-required `TypeId`**

In `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskMappingTests.cs`, the object
initializer for `ExportLoaderTask task = new() { ... }` gains one line. The initializer becomes:

```csharp
ExportLoaderTask task = new()
{
    TypeId = TaskTypeId.BulkImport,
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
```

No other change is needed in this file — the existing
`Assert.Equal(TaskTypeId.BulkImport, reloaded.TypeId)` assertion already covers the discriminator
round-trip.

- [ ] **Step 12: Update `ExportLoaderTaskRepositoryTests.cs`'s `NewTask` helper the same way**

In `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs`, the
private `NewTask` helper's `return new ExportLoaderTask { ... }` gains the same line:

```csharp
private static ExportLoaderTask NewTask(
    string code,
    TaskStateId state = TaskStateId.Ready,
    DateTime? lastStart = null)
{
    DateTime nowUtc = DateTime.UtcNow;
    return new ExportLoaderTask
    {
        TypeId = TaskTypeId.BulkImport,
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
```

No other change is needed in this file.

- [ ] **Step 13: Build again — confirm the test project compiles**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: succeeds with no errors.

- [ ] **Step 14: Run the full test suite against a real Postgres (Testcontainers)**

Run: `dotnet test src/Abm.PD/Abm.PD.slnx`
Expected: all tests pass, including `ExportLoaderTaskMappingTests`, `ExportLoaderTaskRepositoryTests`,
and `TaskLookupSeedTests` (the last of these is unaffected by this change but proves the
`IntegrationTestFixture`'s migration-apply-then-seed-check path still works against the regenerated
`InitialCreate`). Docker must be running for Testcontainers to start Postgres — if it isn't, start it
first.

- [ ] **Step 15: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskBase.cs \
        src/Abm.PD/Abm.PD.Core.Domain/Entities/ExportLoaderTask.cs \
        src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskBaseConfiguration.cs \
        src/Abm.PD/Abm.PD.Core.Repository/Configuration/ExportLoaderTaskConfiguration.cs \
        src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs \
        src/Abm.PD/Abm.PD.Core.Repository/Migrations/ \
        src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskMappingTests.cs \
        src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs \
        docs/superpowers/specs/2026-09-12-task-tph-reversal-design.md \
        docs/superpowers/plans/2026-09-12-task-tph-reversal.md
git rm src/Abm.PD/Abm.PD.Core.Repository/Configuration/TpcOwnedEntityKeyNameFixupConvention.cs \
       src/Abm.PD/Abm.PD.Core.Api.Tests/Conventions/TpcOwnedEntityKeyNameFixupConventionTests.cs
git commit -m "$(cat <<'EOF'
Move TaskBase/ExportLoaderTask from TPC to TPH mapping

TypeId is now a stored column and the TPH discriminator; Code, DisplayName
and Description move onto TaskBase as plain properties; ExportParameter
moves into its own table. Drops the now-unnecessary
TpcOwnedEntityKeyNameFixupConvention and regenerates a single InitialCreate
migration in place of the five TPC-era migrations.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UMWQENpvJ4gbbbbtmhodF8
EOF
)"
```

---

## Self-Review Notes

- **Spec coverage:** every bullet in the spec's Purpose section maps to a step above — stored
  `TypeId` discriminator (Steps 1, 4), `Code`/`DisplayName`/`Description` on `TaskBase` (Step 1),
  shared `task` table (Step 4), `ExportParameter` in its own table (Step 5), dropped/regenerated
  migrations (Steps 9–10), deleted TPC convention and its test (Steps 6–7).
- **Placeholder scan:** no TBD/TODO markers; every step shows the full file content or the exact CLI
  command, not a description of what to do.
- **Type consistency:** `TaskBase.TypeId`/`Code`/`DisplayName`/`Description` are declared once in Step
  1 and referenced with the same names throughout; `ExportLoaderTaskConfiguration`'s column names
  (`type`/`since`/`type_filter_list`) match what Step 5 declares and what Step 10's verification
  checks for.
