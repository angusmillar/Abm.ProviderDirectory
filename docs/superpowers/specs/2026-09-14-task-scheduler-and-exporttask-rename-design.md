# Generic task scheduler, generic task repository, and ExportLoaderTask → ExportTask rename

Date: 2026-09-14
Status: Approved for planning

## Purpose

Two related renames/refactors, agreed together because the second touches nearly every file the first
does:

1. **Generic scheduler + repository.** `ExportLoaderTaskScheduler` and `IExportLoaderTaskRepository`
   are named and typed as if `ExportLoaderTask` were the only kind of scheduled work `TaskBase` (already
   TPH-mapped, see the 2026-09-12 TPH reversal design) will ever describe. The scheduling mechanics
   (find due, claim, reap stale, record outcome) never actually touch anything `ExportLoaderTask`-specific
   — they only read/write `TaskBase` columns. This design splits that scheduling surface into a new,
   genuinely generic `ITaskRepository`/`TaskRepository` and renames the scheduler to `TaskScheduler`, so
   a second `TaskBase` subtype in future only needs a new branch in the scheduler's dispatch, not a new
   scheduler.
2. **`ExportLoaderTask` → `ExportTask` rename**, flowed through the entity, its repository, EF
   configuration (including the physical owned-type table and FK constraint), the CRUD API endpoints and
   route (`/ExportLoaderTask` → `/ExportTask`), request/response contracts, and every test that touches
   any of the above. Pure rename — no behavioural change beyond the physical DB objects moving name.

Both land together: since we're still in startup/dev mode (per the 2026-09-13 scheduler design's own
migrations, all dated the last two days), the three existing migrations are deleted and replaced by one
fresh `InitialCreate` reflecting the renamed schema, rather than carrying the old name through a rename
migration.

## Part 1 — Generic `ITaskRepository` and `TaskScheduler`

### Why a query against `TaskBase` doesn't need to know about subtype navigations

TPH means `TaskBase`, `ExportTask`, and any future subtype share one `task` table, discriminated by
`TypeId`. Querying `dbContext.Set<TaskBase>()` issues one `SELECT` that already carries every subtype's
own scalar and owned-type columns (owned types, like `ExportTask.Parameter`, are always eagerly loaded
regardless of `Include`) — so a `TaskBase` element cast to `ExportTask` after such a query already has
its own scalar/owned data fully populated, no extra query needed.

What it *can't* give you is a subtype's non-owned reference navigation — `ExportTask.DataSource` — since
`IQueryable<TaskBase>` has no compile-time way to `Include` a property only `ExportTask` declares. Rather
than force the generic repository to learn about every subtype's eager-loading needs (via a cast-`Include`
or a magic Include string), `ITaskRepository` simply never deals in navigations at all — it only ever
reads/writes `TaskBase`-level columns (`Id`, `State`, `LastStart`, `TriggerEvery`, `FailureCount`, ...).
When `TaskScheduler` needs the fully-loaded entity to actually do the work, it re-fetches by `Id` through
the existing type-specific repository (`IExportTaskRepository.GetByIdAsync`, which already
`.Include(x => x.DataSource)`s). That re-fetch is one indexed PK lookup on a low-frequency poll loop —
cheap — and it keeps `IExportTaskRepository` as the single place that knows how to fully load an
`ExportTask`.

### `ITaskRepository` (new, `Abm.PD.Core.Domain/Repositories/ITaskRepository.cs`)

```csharp
public interface ITaskRepository
{
    Task<IReadOnlyList<TaskBase>> FindDueAsync(
        DateTime nowUtc,
        int failureAttemptCount,
        CancellationToken cancellationToken);

    Task<bool> TryClaimAsync(
        int id,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task ReapStaleInProgressAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken);

    Task RecordOutcomeAsync(
        int id,
        TaskStateId state,
        DateTime nowUtc,
        string? stateReason,
        FailureCountUpdate failureCountUpdate,
        CancellationToken cancellationToken);
}
```

These four methods are removed from `IExportLoaderTaskRepository` (→ `IExportTaskRepository`, Part 2),
which keeps `GetAllAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `SearchAsync` —
unchanged signatures, the CRUD API's contract is untouched by Part 1.

### `TaskRepository` (new, `Abm.PD.Core.Repository/Repositories/TaskRepository.cs`)

Same query/update logic as today's `ExportLoaderTaskRepository.FindDueAsync`/`TryClaimAsync`/
`ReapStaleInProgressAsync`/`RecordOutcomeAsync`, retargeted from `dbContext.ExportLoaderTasks` to a new
`dbContext.Tasks` (`DbSet<TaskBase> Tasks => Set<TaskBase>();`, added to `ProviderDirectoryDbContext`
alongside the renamed `DbSet<ExportTask> ExportTasks`) and returning `TaskBase` from `FindDueAsync`. No
`.Include()` calls anywhere in this class — it never touches navigations, per the above.

`ExportTaskRepository` (Part 2's renamed `ExportLoaderTaskRepository`) loses these four methods
entirely — this also resolves the pre-existing uncommitted syntax error on that file
(`Task<IReadOnlyList<ExportLoaderTask>FindDueAsync(` — missing `>`), since the broken method leaves the
file as part of this same edit.

DI (`Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs`):
`services.AddScoped<ITaskRepository, TaskRepository>();` alongside the existing
`IExportTaskRepository` registration.

### `TaskScheduler` (renamed from `ExportLoaderTaskScheduler`)

`Abm.PD.Core.Application/ExportLoaderTaskScheduler.cs` → `TaskScheduler.cs`. Injects `ITaskRepository`
(scheduling) and `IExportTaskRepository` (the re-fetch), plus the existing `IServiceScopeFactory`,
`IDateTimeProvider`, `IOptions<TaskSchedulerSettings>`, `ILogger<TaskScheduler>`.

`DoWork`, per due `TaskBase`:

1. `ReapStaleInProgressAsync` / `FindDueAsync` as today, now via `ITaskRepository`, generic over
   `TaskBase`.
2. `TryClaimAsync(task.Id, ...)` as today — generic, keyed by `Id` alone.
3. Dispatch on the claimed row's runtime type:
   - `is ExportTask` → the pattern-matched `exportTask` from this `is` check is used **only** to
     identify the branch and read `task.Id` — never passed to `IExportRunner.Run` (see the callout
     below for why). Instead: `await exportTaskRepository.GetByIdAsync(task.Id, ct)` (non-null — just
     claimed) for a second, fully-loaded instance with `DataSource` populated, and *that* instance is
     what gets passed to the existing scoped `IExportRunner.Run` exactly as today (signature becomes
     `Run(ExportTask, ct)` — see Part 2).
   - unrecognised `TypeId` → log a warning and `RecordOutcomeAsync(..., TaskStateId.Failed,
     $"Unsupported task type {task.TypeId}", FailureCountUpdate.Increment, ...)` rather than throwing —
     a future subtype the scheduler doesn't yet know how to run fails loudly and moves on, instead of
     sitting claimed forever.
4. `RecordOutcomeAsync` as today, keyed by `task.Id` — unchanged, already `TaskBase`-level.

**Why the claimed instance can't be used directly.** A row materialised off `ITaskRepository` (backed by
`Set<TaskBase>()`, no `.Include()`s) has its scalar columns and owned types fully populated — TPH puts
every subtype's own scalar columns on the one `task` table, and owned types like `ExportTask.Parameter`
are always eagerly joined regardless of `Include` — so `exportTask.DataSourceId` and `exportTask.Parameter`
off that instance are correct. `exportTask.DataSource` is not: it's a genuine reference navigation to a
separate `DataSource` entity, not an owned type, and nothing `Include`d it. Despite `ExportTask.DataSource`
being declared `public required DataSource DataSource { get; set; }`, that `required` is enforced by the
compiler only against `new`-expressions and constructors in hand-written code — EF Core's materialiser
doesn't go through that check, so it will silently leave `DataSource` as `null` on an instance it builds
without an `Include`. `ExportRunner.Run` dereferences `exportTask.DataSource`, so calling it with the
claimed-but-not-refetched instance would throw a `NullReferenceException` at runtime despite the type
system claiming that can't happen. The re-fetch through `IExportTaskRepository.GetByIdAsync` (which does
`.Include(x => x.DataSource)`) is the only instance in this flow that's safe to hand to `Run`.

### Settings rename

`Abm.PD.Core.Application/Settings/ExportLoaderTaskSchedulerSettings.cs` → `TaskSchedulerSettings.cs`;
`SectionName` `"ExportLoaderTaskScheduler"` → `"TaskScheduler"`. `appsettings.json` /
`appsettings.Development.json`: `"ExportLoaderTaskScheduler": { ... }` → `"TaskScheduler": { ... }`.
`Abm.PD.Core.Application/DependencyInjection/ServiceCollectionExtension.cs` updated to the renamed
types (`AddTimedHostedService<TaskScheduler>()` etc.).

### The `TaskScheduler` / `System.Threading.Tasks.TaskScheduler` name collision

`ImplicitUsings` brings `System.Threading.Tasks` (and its `TaskScheduler`) into every file's using list.
C# resolves an unqualified simple name by first searching the *enclosing namespace chain* — every
namespace that is a dotted ancestor of the current one — before consulting `using` directives at all,
and a match found via that chain wins outright with no ambiguity check against usings.

Concretely: our new `TaskScheduler` lives in `Abm.PD.Core.Application`. Any file whose own namespace is
`Abm.PD.Core.Application` or a dotted descendant of it (e.g. `Abm.PD.Core.Application.DependencyInjection`,
`Abm.PD.Core.Application.Tests`) finds it via the chain — no collision, no explicit `using` even needed.
Only a file *outside* that chain that references the type via an explicit `using Abm.PD.Core.Application;`
hits genuine ambiguity, because at that point both the explicit using and the implicit global
`System.Threading.Tasks` using are competing at the same tier. Auditing every current reference to
`ExportLoaderTaskScheduler`, exactly one file is outside the chain and uses an explicit `using`:
`Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskSchedulerTests.cs` (namespace
`Abm.PD.Core.Api.Tests.ExportLoaderTasks`). Every reference to the type there becomes fully-qualified —
`Abm.PD.Core.Application.TaskScheduler` — rather than adding a file-scoped alias, since it's self-
explanatory and it's the only file affected.

## Part 2 — `ExportLoaderTask` → `ExportTask` rename

Pure rename, flowed through every layer. No behavioural change other than the physical DB object names
(below).

**Domain**
- `Entities/ExportLoaderTask.cs` → `ExportTask.cs`, class `ExportLoaderTask` → `ExportTask`.
- `Repositories/IExportLoaderTaskRepository.cs` → `IExportTaskRepository.cs` (already shrunk to CRUD-only
  by Part 1).

**Repository**
- `Repositories/ExportLoaderTaskRepository.cs` → `ExportTaskRepository.cs` (already shrunk by Part 1).
- `Configuration/ExportLoaderTaskConfiguration.cs` → `ExportTaskConfiguration.cs`:
  - Owned `Parameter` table `export_loader_task_parameter` → `export_task_parameter`.
  - FK constraint name `fk_export_loader_task_parameter_task` → `fk_export_task_parameter_task`.
- `TaskBaseConfiguration.cs`: `.HasValue<ExportLoaderTask>(TaskTypeId.BulkImport)` →
  `.HasValue<ExportTask>(TaskTypeId.BulkImport)`.
- `ProviderDirectoryDbContext.cs`: `DbSet<ExportLoaderTask> ExportLoaderTasks` →
  `DbSet<ExportTask> ExportTasks` (plus the new `DbSet<TaskBase> Tasks` from Part 1).

**Migrations** — deleted and replaced, not renamed-in-place, per the "still very much in startup
development mode" decision:
- Delete `20260913120952_InitialCreate`, `20260913135635_AddSourceResource`,
  `20260914083335_AddTaskFailureCount` (each `.cs` + `.Designer.cs`) and
  `ProviderDirectoryDbContextModelSnapshot.cs`.
- Once every rename above is in place, regenerate a single fresh migration:
  `dotnet ef migrations add InitialCreate --project Abm.PD.Core.Repository --startup-project Abm.PD.Core.Repository`
  (per `CLAUDE.md`'s stated convention). Generating migration files does not require a database
  connection, so this is safe to run as part of implementation.
- Applying it (`dotnet ef database update`) against any real local dev database, and dropping/recreating
  that database first so its migration history matches the single new migration, is **not** done as part
  of this work — it's a manual step for whoever owns that database, called out here so it isn't a
  surprise.

**Api**
- `Endpoints/ExportLoaderTaskEndpoints.cs` → `ExportTaskEndpoints.cs`; class `ExportLoaderTaskEndpoints`
  → `ExportTaskEndpoints`; routes `/ExportLoaderTask`, `/ExportLoaderTask/{id:int}` →
  `/ExportTask`, `/ExportTask/{id:int}` (GET all-or-search, GET by id, POST, PUT, DELETE — all five).
- `Contracts/ExportLoaderTaskRequest.cs`, `ExportLoaderTaskResponse.cs`,
  `ExportLoaderTaskUpdateRequest.cs`, `ExportLoaderTaskParameterRequest.cs` → `ExportTaskRequest.cs`,
  `ExportTaskResponse.cs`, `ExportTaskUpdateRequest.cs`, `ExportTaskParameterRequest.cs` (types renamed
  to match).
- `Program.cs`: `app.MapExportLoaderTaskEndpoints();` → `app.MapExportTaskEndpoints();`.

**Application**
- `IExportRunner.Run(ExportLoaderTask exportLoaderTask, CancellationToken ct)` →
  `Run(ExportTask exportTask, CancellationToken ct)`; `ExportRunner.cs` updated to match (parameter name
  and every reference inside the method body).
- `TaskScheduler` (Part 1) dispatches on `is ExportTask` rather than `is ExportLoaderTask`.

**Tests**
- `Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskCrudTests.cs` → `ExportTaskCrudTests.cs`.
- `Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskMappingTests.cs` → `ExportTaskMappingTests.cs`.
- `Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs` splits in two:
  - CRUD/Search cases (`AddAsync`, `GetByIdAsync`, `GetAllAsync`, `UpdateAsync`, `DeleteAsync`,
    `SearchAsync`) → `ExportTaskRepositoryTests.cs`, against `IExportTaskRepository`, otherwise
    unchanged.
  - Scheduling cases (`FindDueAsync`, `TryClaimAsync`, `ReapStaleInProgressAsync`, `RecordOutcomeAsync`)
    → new `TaskRepositoryTests.cs`, against `ITaskRepository`, resolving `TaskBase` rows (test bodies
    still construct `ExportTask` instances to seed rows, since `TaskBase` is abstract, but assert only
    on `TaskBase`-level fields).
- `ExportLoaderTaskSchedulerTests.cs` in both `Abm.PD.Core.Application.Tests` and
  `Abm.PD.Core.Api.Tests/ExportLoaderTasks/` → `TaskSchedulerTests.cs`. The `Api.Tests` copy needs the
  fully-qualified `Abm.PD.Core.Application.TaskScheduler` per the collision note above; the
  `Application.Tests` copy resolves unqualified (same namespace chain).
- `Abm.PD.Core.Application.Tests/TestDoubles/InMemoryExportLoaderTaskRepository.cs` → replaced by two
  narrower doubles: an `InMemoryTaskRepository` implementing `ITaskRepository` over `List<TaskBase>`
  (`FindDueAsync`/`TryClaimAsync`/`ReapStaleInProgressAsync`/`RecordOutcomeAsync`, same logic as today),
  and a minimal `InMemoryExportTaskRepository` implementing `IExportTaskRepository` with a working
  `GetByIdAsync` — unlike today's fake, this one is *not* a throw-everywhere stub, because
  `TaskScheduler` unconditionally calls it for the re-fetch (see the callout above): every
  `TaskSchedulerTests` case needs `GetByIdAsync` to return the seeded `ExportTask` complete with its
  `DataSource` set, or the test would pass for the wrong reason — `IExportRunner` fakes
  (`ThrowingExportRunner`/`ScopeTrackingExportRunner`) don't happen to touch `.DataSource`, so a fake
  that silently returned an instance with `DataSource` null wouldn't fail today's assertions but would
  hide a real bug. The remaining CRUD methods can stay `NotImplementedException` stubs, as they are
  today, since nothing in this test class calls them.
- `Abm.PD.Core.Application.Tests/TestDoubles/ThrowingExportRunner.cs`,
  `ScopeTrackingExportRunner.cs`, `Abm.PD.Core.Api.Tests/TestDoubles/ConfigurableExportRunner.cs`:
  `Run(ExportLoaderTask, ct)` signatures → `Run(ExportTask, ct)`.
- `Abm.PD.Core.Application.Tests/ExportRunnerTests.cs`: entity type references updated.
- `Abm.PD.Core.Api.Tests/Fixtures/CoreApiWebApplicationFactory.cs`: config keys
  `"ExportLoaderTaskScheduler:PollInterval"` / `"...:StaleInProgressAfter"` → `"TaskScheduler:..."`
  (Part 1's settings rename, not a Part 2 change, but lands in the same file).
- New regression case in the `Abm.PD.Core.Api.Tests` copy of `TaskSchedulerTests`: assert that the
  `ExportTask` reaching `IExportRunner.Run` has a non-null `DataSource` — `ConfigurableExportRunner`'s
  `Behaviour` callback receives the task passed to `Run`, so the assertion goes inside it. This is the
  concrete test for the re-fetch behaviour described in the callout above; it would fail if a future
  change accidentally passed the claimed-but-not-refetched instance to `Run` instead.

## Out of scope

- Renaming `TaskTypeId.BulkImport` or any other enum member — unrelated to this rename, not requested.
- Retiring `Abm.PD.Console`/`ConsoleApplication`'s manual run path.
- Applying the regenerated migration to any real database, or dropping/recreating a local dev database —
  called out above as a manual follow-up, not part of this work.
- Adding a second `TaskBase` subtype — Part 1 makes the scheduler/repository ready for one, but none is
  being added here.
