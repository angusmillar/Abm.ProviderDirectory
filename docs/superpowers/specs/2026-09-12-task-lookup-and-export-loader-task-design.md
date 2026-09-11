# Task lookup enums + ExportLoaderTask/ExportParameter repository wiring

Date: 2026-09-12
Status: Approved for planning

## Purpose

Two related pieces of work in `Abm.PD.Core.Domain` / `Abm.PD.Core.Repository`:

1. Map `TaskStatusId` and `TaskTypeId` as EF Core lookup entities, each with its own seeded table,
   following the pattern already used in the sibling `PyroServer` solution for `HttpVerb`.
2. Wire the existing `TaskBase` / `ExportLoaderTask` / `ExportParameter` entities into the repository
   layer: decide how EF Core maps the `TaskBase` → `ExportLoaderTask` class hierarchy, add an
   `IExportLoaderTaskRepository`/`ExportLoaderTaskRepository` covering CRUS of both the parent task
   and its child parameter, and decide how `ExportParameter.TypeFilterList` (a `List<string>`) is
   stored.

## Reference pattern: PyroServer's `HttpVerb`

`HttpVerb` (`Abm.Pyro.Domain.Model.HttpVerb`) is the precedent this follows:

- A class keyed on the enum itself, not a separate identity column: `HasKey(x => x.HttpVerbId)`,
  `Property(x => x.HttpVerbId).HasConversion<int>()`.
- Seeded via `HasData(Enum.GetValues(typeof(HttpVerbId)).Cast<HttpVerbId>().Select(e => new HttpVerb(e, e.ToString())))`
  — one row per enum member, `Name` set to the enum member's own name.
- Consumers (e.g. `ResourceStore.HttpVerb`) declare a plain enum-typed property with
  `builder.Property(x => x.HttpVerb)` and nothing else — **no navigation property and no FK
  constraint** is created. The column is simply an `int` shaped like the lookup table's key; nothing
  in the schema enforces that the value exists in `HttpVerb`. This repo follows that same convention:
  lookup tables are seeded reference/descriptive data, not referentially enforced from consuming
  tables.

## `TaskStatus` / `TaskType` lookup entities

New entities in `Abm.PD.Core.Domain/Entities/`:

```csharp
public class TaskStatus
{
    private TaskStatus() { }
    public TaskStatus(TaskStatusId taskStatusId, string name)
    {
        TaskStatusId = taskStatusId;
        Name = name;
    }
    public TaskStatusId TaskStatusId { get; set; }
    public string Name { get; set; }
}
```

(`TaskType` is the same shape, keyed on `TaskTypeId`.) Both are plain classes — not records — for the
same reason `Resource`/`ProviderDataSource` are: EF Core change tracking wants mutable properties.

EF configuration (`Abm.PD.Core.Repository/Configuration/TaskStatusConfiguration.cs`,
`TaskTypeConfiguration.cs`), mirroring `HttpVerbEntityConfig` exactly:

```csharp
builder.ToTable("task_status");
builder.HasKey(x => x.TaskStatusId);
builder.Property(x => x.TaskStatusId).HasConversion<int>();
builder.HasData(
    Enum.GetValues(typeof(TaskStatusId))
        .Cast<TaskStatusId>()
        .Select(e => new TaskStatus(e, e.ToString())));
```

`task_type` is seeded the same way even though nothing references it by FK yet (see TPC section
below — `TypeId` is not a persisted column on `ExportLoaderTask`). It exists now as reference data
for display/lookup purposes and so the next `TaskBase` subtype has it ready.

## `TaskBase.Status` type fix

`TaskBase.Status` is currently typed `System.Threading.Tasks.TaskStatus` (aliased at the top of
`TaskBase.cs` to dodge the name clash with the new domain `TaskStatus` type) — the BCL's task
execution-state enum (`Created`, `RanToCompletion`, `Faulted`, etc.), not the domain status
(`Ready`/`InProgress`/`OnHold`/`Completed`/`Failed`). This was a mistake, not intentional design.
Fixed as part of this work: `Status` becomes `TaskStatusId`, and the `using TaskStatus = ...` alias is
removed. Because the domain entity `TaskStatus` (the lookup row type, see above) and the enum
`TaskStatusId` are different types, there is no naming collision to alias around any more.

## `TaskBase` / `ExportLoaderTask`: TPC mapping

`TaskBase` is abstract; `ExportLoaderTask` is its only concrete subtype today, with room in
`TaskTypeId` for more later. Mapping strategy: **Table-Per-Concrete-Type (TPC)**, EF Core's
`UseTpcMappingStrategy()` (available since EF Core 7):

```csharp
modelBuilder.Entity<TaskBase>().UseTpcMappingStrategy();
```

with `ExportLoaderTask` configured as a concrete leaf mapped to its own table, `export_loader_task`,
carrying every `TaskBase` column (`id`, `code`, `display_name`, `description`, `status`,
`status_reason`, `trigger_every`, `to_start_at_utc`, `to_end_at_utc`, `created_utc`, `updated_utc`,
`last_start`, `last_end`) plus its own (the owned `Parameter`, see below). There is no shared `task`
table.

Consequences accepted as part of this decision:

- No cross-type task table or query. If a future need arises to look up "a task, any type" generically
  (e.g. an audit/log table keyed by task id across types), that will require either a shared ID
  sequence across all `TaskBase`-derived tables or a different design at that point — not needed
  today, so not built now.
- Each concrete table gets ordinary per-table identity (`id` starting at 1, independently, per table).
  Since nothing references tasks generically, this is a non-issue.
- Querying `TaskBase` directly (not needed by anything in this design) would compile to a
  `UNION ALL` across concrete tables once more than one exists — fine for an occasional admin query,
  not a pattern to build on.

`TaskBase.TypeId` (abstract, computed: `public override TaskTypeId TypeId => TaskTypeId.BulkImport;`)
is **not mapped as a column**. Under TPC, the table itself already identifies the concrete type — a
constant `type_id` value repeated on every row of `export_loader_task` would be pure redundancy.
Excluded via `modelBuilder.Entity<ExportLoaderTask>().Ignore(x => x.TypeId)`.

`Status` is mapped as a plain property (`builder.Property(x => x.Status)`), converted to `int` by EF
Core convention — no FK, no navigation property, per the PyroServer convention above.

## `ExportParameter`: owned dependent, not an independent entity

`ExportParameter` currently has its own `Id`. That identity is dropped — as a required 1:1 child of
`ExportLoaderTask` that never exists independently, it is mapped as an EF **owned entity**:

```csharp
modelBuilder.Entity<ExportLoaderTask>().OwnsOne(x => x.Parameter, p =>
{
    p.Property(x => x.Type).HasColumnName("parameter_type");
    p.Property(x => x.Since).HasColumnName("parameter_since");
    p.Property(x => x.TypeFilterList).HasColumnName("parameter_type_filter_list");
});
```

Its columns fold into `export_loader_task` — no separate table, no join, one row per task carries its
parameter inline. `ExportParameter.Id` is removed from the entity (owned types are identified by their
owner; they don't need their own key). Load, save, and delete of the parameter always happen as part
of saving/deleting the owning `ExportLoaderTask` — there is no independent CRUD path for it, matching
the "CRUS of both the parent and child, from one repository" requirement.

## `ExportParameter.TypeFilterList`: native Postgres array

`TypeFilterList` (`List<string>`) is mapped to a native Postgres `text[]` column. Npgsql's EF Core
provider maps `List<string>` (and other primitive collections) to `text[]` by convention — no value
converter, no JSON (de)serialization, and the column remains queryable with Postgres array operators
if that's ever useful. This was chosen over a `jsonb`/`ToJson()` owned-collection mapping because
Postgres is the only target and a real array is the more native fit than a JSON-encoded one.

## `IExportLoaderTaskRepository`

```csharp
public interface IExportLoaderTaskRepository
{
    Task<IReadOnlyList<ExportLoaderTask>> GetAllAsync(CancellationToken cancellationToken);

    Task<ExportLoaderTask?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<ExportLoaderTask> AddAsync(ExportLoaderTask task, CancellationToken cancellationToken);

    Task<ExportLoaderTask?> UpdateAsync(int id, ExportLoaderTask task, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExportLoaderTask>> SearchAsync(
        string? code,
        TaskStatusId? status,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken);
}
```

This departs from `IProviderDataSourceRepository`'s flat-scalar-parameter convention: `AddAsync`/
`UpdateAsync` take the whole `ExportLoaderTask` object (including its required `Parameter`), because
there is no sane scalar-only signature for a required owned value with a list field. `UpdateAsync`
loads the tracked entity by `id`, copies every writable field from the passed-in object (`Code`,
`DisplayName`, `Description`, `Status`, `StatusReason`, `TriggerEvery`, `ToStartAtUtc`, `ToEndAtUtc`,
`LastStart`, `LastEnd`, and the owned `Parameter`'s `Type`/`Since`/`TypeFilterList`), then
`SaveChangesAsync` — one call updates parent and owned child together since they live in one table.

`SearchAsync` filters: `code` (exact match, same convention as `ProviderDataSourceRepository`),
`status` (exact match against `TaskStatusId`), and a `lastStartFrom`/`lastStartTo` range against
`LastStart` (each bound optional, applied independently when supplied) — the three filter vectors
requested for finding a task by code, all tasks in a given status, or tasks that last ran in a window.

## Migration

One new migration adding `task_status` and `task_type` (both seeded via `HasData`) and
`export_loader_task` (the TPC leaf table, with the owned `ExportParameter` columns flattened in,
including the `text[]` `parameter_type_filter_list` column). No explicit `ToTable`/`HasColumnName`
calls are needed beyond what's shown above — `UseSnakeCaseNamingConvention()` already produces
`task_status`, `task_type`, `export_loader_task`, `display_name`, etc. by convention.

## Integration test fixture: seeded lookup tables vs `Respawner`

`Abm.PD.Core.Api.Tests/Fixtures/IntegrationTestFixture.cs` resets every table in the `public` schema
between tests via an ignore-list, with the explicit stated reason (in its own comment) that "this
schema seeds no data that needs to survive a reset". That stops being true once `task_status`/
`task_type` are seeded via migration `HasData` — without a change, `ResetDatabaseAsync()` would
truncate that seed data after the first test that touches it, leaving later tests with an empty
lookup table.

Fix: add `task_status` and `task_type` to the existing `TablesToIgnore` list alongside
`__ef_migrations_history`, with a short comment explaining why (seeded reference data, not test data).
This keeps the ignore-list approach rather than switching to PyroServer's allow-list style.

## Testing

`ExportLoaderTaskRepository` (owned-entity mapping, TPC, `text[]` column, and the lookup tables it
sits alongside) is exercised through the same Testcontainers-backed Postgres integration pattern
`ProviderDataSourceCrudTests` already establishes, rather than through a mocked `DbContext` — these
are exactly the kind of mapping decisions (TPC, owned types, native array columns) that only a real
provider proves out.

## Out of scope

- API endpoints for `ExportLoaderTask` (route conventions, contracts) — this design covers the
  repository layer only.
- Any second `TaskBase` subtype — `TaskType` is seeded and ready, but no second concrete task type is
  being added now.
- FK constraints from any consuming column back to `task_status`/`task_type` — deliberately not used,
  per the PyroServer convention this follows.
