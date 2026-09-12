# TaskBase/ExportLoaderTask: TPC → TPH reversal

Date: 2026-09-12
Status: Approved for planning
Supersedes: the "TaskBase / ExportLoaderTask: TPC mapping" and "ExportParameter: owned dependent, not
an independent entity" sections of
`docs/superpowers/specs/2026-09-12-task-lookup-and-export-loader-task-design.md`

## Purpose

Reverse the Table-Per-Concrete-Type mapping decision made earlier the same day for the
`TaskBase`/`ExportLoaderTask` hierarchy. Move to Table-Per-Hierarchy (TPH):

1. `TaskBase.TypeId` becomes a real, stored column and the TPH discriminator — no longer an
   abstract, per-subtype computed property.
2. `Code`, `DisplayName`, `Description` move onto `TaskBase` as ordinary (non-abstract,
   non-overridden) properties. `ExportLoaderTask` no longer redeclares them.
3. All `TaskBase` subtypes (today, only `ExportLoaderTask`) share one table, `task`.
4. `ExportParameter` stays an EF owned entity of `ExportLoaderTask.Parameter`, but moves into its own
   table, `export_loader_task_parameter`, instead of folding its columns into the owner's table.
5. Every existing migration is dropped and replaced with a single fresh `InitialCreate`.

## Why TPH instead of TPC

TPC gave every concrete task type its own table and required a shared cross-table identity
sequence (`TaskBaseSequence`) plus a bespoke model-finalizing convention
(`TpcOwnedEntityKeyNameFixupConvention`) purely to reconcile the primary-key constraint name between
a TPC leaf table and a table-split owned entity sharing it — see that class's own doc comment for the
full mechanics. That convention explicitly does not generalise past one concrete subtype and throws
rather than silently mis-scaffolding a second one.

TPH removes both problems at the root: one shared table means one shared identity column (Postgres's
ordinary `IdentityByDefaultColumn`, no sequence juggling), and moving `ExportParameter` into its own
table means it is no longer table-split against anything, so the PK-name collision
`TpcOwnedEntityKeyNameFixupConvention` exists to fix cannot occur — the convention and its dedicated
regression test class are deleted outright, not reworked.

## `TaskBase`: stored `TypeId`, plain `Code`/`DisplayName`/`Description`

```csharp
public abstract class TaskBase
{
    protected TaskBase(TaskTypeId typeId)
    {
        TypeId = typeId;
    }

    public int Id { get; set; }

    public TaskTypeId TypeId { get; private set; }

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

`TypeId` was `public abstract TaskTypeId TypeId { get; }`, overridden in `ExportLoaderTask` as
`=> TaskTypeId.BulkImport` and explicitly `Ignore()`d in `ExportLoaderTaskConfiguration` because a TPC
leaf table already identifies its own concrete type. Under TPH every subtype shares one table, so the
discriminator has to be a real, stored column. It is deliberately **not** a publicly settable
`required` property, though: EF Core does not manage a discriminator mapped to a real CLR property the
way it manages a shadow one — a `ValueGenerated.Never` property keeps whatever value the caller last
set, so a public setter would let a mismatched `TypeId` be written, silently producing a row that no
`ExportLoaderTaskRepository` query can ever find again (every query implicitly filters on the correct
`type_id`) while still occupying its slot in the unique `Code` index. Instead, `TaskBase` takes
`typeId` as a constructor parameter (`protected TaskBase(TaskTypeId typeId)`) and exposes it as
`TaskTypeId TypeId { get; private set; }` — EF Core reads/writes private setters via reflection during
materialisation without issue, so the column mapping and discriminator configuration are unaffected;
only external code loses the ability to set it. `ExportLoaderTask` no longer overrides `TypeId`,
`Code`, `DisplayName`, or `Description` — `Code`/`DisplayName`/`Description` are inherited as-is, and
`TypeId` is fixed by `ExportLoaderTask`'s own constructor.

`ExportLoaderTask` shrinks to just its own data plus the constructor that fixes its `TypeId`:

```csharp
public class ExportLoaderTask : TaskBase
{
    public ExportLoaderTask()
        : base(TaskTypeId.BulkImport)
    {
    }

    public required ExportParameter Parameter { get; set; }
}
```

## EF mapping: one `task` table, `TypeId` as discriminator

`TaskBaseConfiguration` (was: `UseTpcMappingStrategy()`) now configures the shared table, the
`Code` uniqueness constraint (moved here from `ExportLoaderTaskConfiguration` since `Code` now lives
on `TaskBase`), and the discriminator:

```csharp
builder.ToTable("task");
builder.HasKey(x => x.Id);

builder.Property(x => x.Code)
    .HasMaxLength(EntityConfigurationConstants.CodeMaxLength);

builder.HasIndex(x => x.Code)
    .IsUnique();

builder.HasDiscriminator(x => x.TypeId)
    .HasValue<ExportLoaderTask>(TaskTypeId.BulkImport);
```

The `ix_task_code` uniqueness constraint is now hierarchy-wide — it spans every `TaskBase` subtype
sharing the `task` table, not just `ExportLoaderTask` rows, since under TPH there is only the one
table and one index to enforce it against. This is the intended semantics of moving `Code` onto
`TaskBase`, just previously unstated: it was per-concrete-type under TPC (each subtype had its own
table and its own uniqueness constraint) and is now shared across the whole hierarchy.

`ExportLoaderTaskConfiguration` shrinks to just the owned `Parameter` mapping, now into its own table
rather than table-split into `task`:

```csharp
builder.OwnsOne(x => x.Parameter, parameter =>
{
    parameter.ToTable("export_loader_task_parameter");
    parameter.WithOwner().HasConstraintName("fk_export_loader_task_parameter_task");
    parameter.Property(x => x.Type).HasColumnName("type");
    parameter.Property(x => x.Since).HasColumnName("since");
    parameter.Property(x => x.TypeFilterList).HasColumnName("type_filter_list");
});
```

The explicit `HasConstraintName` avoids EF's auto-generated FK name truncating at Postgres's
63-character identifier limit.

The `parameter_` column-name prefix from the TPC design is dropped — it existed only to disambiguate
owned columns folded into the same physical row as `task`'s own columns; in its own table that
disambiguation is unnecessary. EF Core always eagerly loads an owned entity with its owner regardless
of whether it is table-split or in its own table, so `ExportLoaderTaskRepository` needs no `.Include()`
and no other change.

`TpcOwnedEntityKeyNameFixupConvention.cs` and its registration in
`ProviderDirectoryDbContext.ConfigureConventions` are deleted, and with them the whole
`ConfigureConventions` override (nothing else uses it). `TpcOwnedEntityKeyNameFixupConventionTests.cs`
is deleted with the class it tests.

## Migrations: drop and replace with one `InitialCreate`

All five existing migration pairs (`InitialCreate`, `AddProviderDataSource`,
`AddTaskStateAndTaskType`, `AddExportLoaderTask`, `AddExportLoaderTaskCodeConstraint`) plus
`ProviderDirectoryDbContextModelSnapshot.cs` are deleted and replaced with a single freshly generated
`InitialCreate`, produced by the real `dotnet ef migrations add` CLI against
`DesignTimeDbContextFactory` (per `CLAUDE.md`) — never hand-written. This is safe because nothing has
shipped past local/CI Testcontainers databases yet; there is no production data depending on the old
migration history.

The new `InitialCreate` covers every entity that exists today: `resource`, `provider_data_source`,
`task_state`/`task_type` (seeded via `HasData`), the shared `task` table (with `type_id` as a stored
`integer` discriminator column), and `export_loader_task_parameter`.

## Testing

`ExportLoaderTaskMappingTests` and `ExportLoaderTaskRepositoryTests` construct `ExportLoaderTask`
instances via object initializers; since `TypeId` is fixed by `ExportLoaderTask`'s own constructor,
no call site sets it — the object initializers are unchanged from before this reversal apart from
everything else that moved. The existing round-trip assertion
(`Assert.Equal(TaskTypeId.BulkImport, reloaded.TypeId)`) is unchanged in shape but now actually proves
persistence through a real stored column and a fresh `AsNoTracking()` reload, rather than a
guaranteed-correct-by-construction computed override.

## Out of scope

- Any change to `IExportLoaderTaskRepository`'s public shape — it already operates on `TaskBase`
  properties positionally, nothing there depends on TPC vs TPH.
- API endpoints for `ExportLoaderTask` — still not built, per the original spec's "Out of scope".
- A second `TaskBase` subtype — still not being added now; TPH already generalises past one subtype
  far more cleanly than TPC did, but proving that is not part of this reversal.
