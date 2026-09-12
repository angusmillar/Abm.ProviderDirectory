# ExportLoaderTask scheduler

Date: 2026-09-13
Status: Approved for planning

## Purpose

`ExportLoaderTask` rows already carry everything needed to describe a recurring bulk export/load job
(`TriggerEvery`, `ToStartAtUtc`/`ToEndAtUtc`, `State`, `LastStart`/`LastEnd`) and already have a CRUD
API (`ExportLoaderTaskEndpoints`), but nothing runs them. This design adds the piece that actually
fires a due task on its interval and, critically, never lets a still-running previous execution be
joined by a second one — including across multiple `Abm.PD.Core.Api` replicas, since this service may
run scaled out.

Options considered and rejected: a pure in-memory guard (fails across replicas), Quartz.NET/Hangfire/
TickerQ (each brings its own schedule/job schema that would sit alongside, not reuse, `ExportLoaderTask`
— reasonable choices, but this repo already hand-rolls its task model and CLAUDE.md's stated style
favours that over reaching for a library). The approach below keeps Postgres, already a dependency, as
the only coordination point.

## Architecture overview

Three pieces, layered as this repo already layers export/load work:

- **`ExportLoaderTaskScheduler`** (new, `Abm.PD.Core.Application`) — the recurring job. Each tick:
  reap stale `InProgress` rows, find due tasks, claim one at a time, run each via `ExportRunner`,
  record the outcome.
- **`ExportRunner`** (existing, extended) — currently parameterless, hardcodes
  `FhirExportQuery.GetByPostCode()`, and drops the streamed export on the floor instead of loading it.
  This design changes its signature to `Run(ExportLoaderTask task, CancellationToken ct)`, builds the
  `$export` `Parameters` from `task.Parameter` instead of the hardcoded query, and pipes
  `fhirExporter.StreamedExportFileList()` into `IFhirBatchLoader.Load(...)`, returning the
  `FhirBatchLoadResult` to the caller. This is a pre-existing gap in code already being touched, not
  scope creep.
- **Tick engine** (ported from `PyroServer`) — `Abm.Pyro.Application.HostedServiceSupport`'s
  `TimedHostedServiceManager<T>`/`ITimedHostedService` pair, copied into `Abm.Core.HostedService`.
  A single sequential `PeriodicTimer` loop that only calls `WaitForNextTickAsync` again once the
  previous `DoWork` has completed — overlap-with-itself is structurally impossible, no semaphore
  needed. This gives per-process non-overlap for free; cross-replica non-overlap is the DB claim
  below.

## Porting `HostedServiceSupport`

Copy all seven files from `PyroServer`'s
`src\Abm.Pyro.Application\HostedServiceSupport` into
`src\Abm.PD\Abm.Core\HostedService` unchanged apart from namespace:
`Abm.Pyro.Application.HostedServiceSupport` → `Abm.Core.HostedService`.

- `ITimedHostedService`, `TimedHostedServiceManager<T>`, `TimedHostedServiceManagerOptions<T>`,
  `TimedHostedServiceManagerExtensions` (the pattern this design uses).
- `IAppStartupService`, `AppStartupServiceManager<T>`, `AppStartupServiceManagerExtensions` (not
  needed by this design, but part of the same folder — brought across for parity with the source and
  available the next time a blocking startup task is needed).

`Abm.Core.csproj` needs two added package references: `Microsoft.Extensions.Hosting.Abstractions` and
`Microsoft.Extensions.Logging.Abstractions` (it already has `Microsoft.Extensions.Options`;
`Microsoft.Extensions.DependencyInjection.Abstractions` arrives transitively via
`Hosting.Abstractions`, matching how `Abm.Pyro.Application.csproj` itself is set up — no explicit
reference to it there either). No further changes needed for `Abm.Core.HostedService` itself:
`Abm.PD.Core.Application` already reaches `Abm.Core` transitively through its existing reference to
`Abm.PD.BulkExport`, which already references `Abm.Core`.

That transitive path does **not** extend to `ExportLoaderTask`/`IExportLoaderTaskRepository`, though —
those live in `Abm.PD.Core.Domain`, which `Abm.PD.Core.Application` does not currently reference at
all (checked against the actual `.csproj` files: `Abm.PD.Core.Application` today references only
`Abm.PD.BulkExport`; `Abm.PD.Core.Domain` and `Abm.PD.Core.Repository` are referenced solely by
`Abm.PD.Core.Api`). This design adds one new `ProjectReference` — `Abm.PD.Core.Application` →
`Abm.PD.Core.Domain` — so the scheduler and `ExportRunner` can see the entity and the repository
interface. It deliberately does **not** add a reference to `Abm.PD.Core.Repository`: that project
holds the EF Core/`ProviderDirectoryDbContext` specifics, and the scheduler talks only to
`IExportLoaderTaskRepository` (see below), keeping `Abm.PD.Core.Application` free of any EF Core or
Npgsql package reference. `Abm.PD.Core.Api`'s own references are unaffected — it already sees all four
projects — so `Program.cs` wiring needs no new `ProjectReference`, only the new registrations shown
below.

Registration in `Abm.PD.Core.Api/Program.cs`, alongside the other `AddXServices` calls. Two separate
things are being configured here, not one: `ExportLoaderTaskSchedulerSettings` goes through the usual
`AddOptions().Bind(...).ValidateDataAnnotations().ValidateOnStart()` pipeline so the scheduler itself
can inject `IOptions<ExportLoaderTaskSchedulerSettings>` (needed for `StaleInProgressAfter` at each
tick) — but `AddTimedHostedService<T>`'s `configurator` runs synchronously at registration time,
*before* the host is built, so it cannot resolve that same `IOptions<T>` from the container the way
`DatabaseSettings` is read after `builder.Build()` further down this file. `PollInterval` is read
straight off configuration instead, the same value ending up in both places:

```csharp
builder.Services.AddOptions<ExportLoaderTaskSchedulerSettings>()
    .Bind(builder.Configuration.GetSection(ExportLoaderTaskSchedulerSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

ExportLoaderTaskSchedulerSettings schedulerSettings = builder.Configuration
    .GetSection(ExportLoaderTaskSchedulerSettings.SectionName)
    .Get<ExportLoaderTaskSchedulerSettings>() ?? new ExportLoaderTaskSchedulerSettings();

builder.Services.AddScoped<ExportLoaderTaskScheduler>();
builder.Services.AddTimedHostedService<ExportLoaderTaskScheduler>(opt =>
{
    opt.TriggersEvery = schedulerSettings.PollInterval;
});
```

## Settings

New `ExportLoaderTaskSchedulerSettings` (record, following `FhirBatchLoaderSettings`'s shape),
section `ExportLoaderTaskScheduler`, bound with `.ValidateDataAnnotations().ValidateOnStart()` like
`DatabaseSettings`:

```csharp
public record ExportLoaderTaskSchedulerSettings
{
    public const string SectionName = "ExportLoaderTaskScheduler";

    /// <summary>
    /// How often the scheduler checks for due tasks. Independent of any task's own TriggerEvery —
    /// this is the poll granularity, not a schedule.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a task may sit in InProgress before the scheduler assumes its runner crashed or was
    /// killed mid-run (no clean Completed/Failed update ever arrived) and reaps it back to Failed.
    /// Must comfortably exceed the slowest real export/load run, or a live task gets reaped out from
    /// under itself.
    /// </summary>
    [Range(typeof(TimeSpan), "00:05:00", "24:00:00")]
    public TimeSpan StaleInProgressAfter { get; init; } = TimeSpan.FromHours(2);
}
```

`StaleInProgressAfter` is one global value, not per-task — open to revisiting if task run times ever
diverge enough to need it per-row, but nothing today suggests that.

## `IExportLoaderTaskRepository` additions

The scheduler needs four operations the existing repository (`GetAllAsync`, `GetByIdAsync`,
`AddAsync`, `UpdateAsync`, `DeleteAsync`, `SearchAsync`) has no way to express — none of them are a
plain load-mutate-`SaveChangesAsync` round trip, and `SearchAsync`'s filters (`code`, `state`, a
`lastStartFrom`/`lastStartTo` range) can't express "is due" (a per-row comparison of `LastStart +
TriggerEvery` against now) or the start/end scheduling window. New interface members:

```csharp
Task<IReadOnlyList<ExportLoaderTask>> FindDueAsync(DateTime nowUtc, CancellationToken cancellationToken);

Task<bool> TryClaimAsync(int id, DateTime nowUtc, CancellationToken cancellationToken);

Task ReapStaleInProgressAsync(DateTime olderThanUtc, CancellationToken cancellationToken);

Task RecordOutcomeAsync(
    int id,
    TaskStateId state,
    DateTime nowUtc,
    string? stateReason,
    CancellationToken cancellationToken);
```

Implemented in `Abm.PD.Core.Repository`'s `ExportLoaderTaskRepository` — this is the one place that
touches `ProviderDirectoryDbContext`/EF Core specifics; `Abm.PD.Core.Application` never does.

`FindDueAsync`: `State != InProgress`, within `[ToStartAtUtc, ToEndAtUtc]` (null bound = unbounded),
and `LastStart == null || LastStart + TriggerEvery <= nowUtc`.

`TryClaimAsync` — the cross-replica lock, one atomic conditional update:

```csharp
int rows = await dbContext.ExportLoaderTasks
    .Where(t => t.Id == id && t.State != TaskStateId.InProgress)
    .ExecuteUpdateAsync(s => s
        .SetProperty(t => t.State, TaskStateId.InProgress)
        .SetProperty(t => t.LastStart, nowUtc)
        .SetProperty(t => t.StateReason, (string?)null), cancellationToken);
return rows == 1;
```

`true` means this replica won the claim; `false` means another replica already claimed it (or a human
changed its state through the CRUD API) — the scheduler skips silently, no error.

`ReapStaleInProgressAsync`: the same `ExecuteUpdateAsync` shape, sweeping `State == InProgress and
LastStart < olderThanUtc` back to `Failed`, `StateReason = "Reaped: exceeded expected run duration"`.

`RecordOutcomeAsync`: a plain update to `State`/`StateReason` and `LastEnd = nowUtc` on a row this
replica already owns — no race to defend against here, unlike the claim.

## Scheduler tick: reap, find, claim, run, record

```csharp
public class ExportLoaderTaskScheduler(
    IExportLoaderTaskRepository repository,
    ExportRunner exportRunner,
    IDateTimeProvider dateTimeProvider,
    IOptions<ExportLoaderTaskSchedulerSettings> settings,
    ILogger<ExportLoaderTaskScheduler> logger) : ITimedHostedService
{
    public async Task DoWork(CancellationToken cancellationToken)
    {
        DateTime now = dateTimeProvider.UtcNow;
        await repository.ReapStaleInProgressAsync(
            now - settings.Value.StaleInProgressAfter, cancellationToken);

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(now, cancellationToken);
        foreach (ExportLoaderTask task in due)
        {
            if (!await repository.TryClaimAsync(task.Id, now, cancellationToken))
            {
                continue; // another replica (or a human) already claimed/changed it since FindDueAsync
            }

            try
            {
                FhirBatchLoadResult result = await exportRunner.Run(task, cancellationToken);
                await repository.RecordOutcomeAsync(
                    task.Id, TaskStateId.Completed, dateTimeProvider.UtcNow,
                    $"Committed {result.CommittedCount} of {result.SubmittedCount}, {result.FailedCount} failed",
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "ExportLoaderTask {TaskCode} failed", task.Code);
                await repository.RecordOutcomeAsync(
                    task.Id, TaskStateId.Failed, dateTimeProvider.UtcNow,
                    exception.Message, cancellationToken);
            }
        }
    }
}
```

Due tasks are claimed and run one at a time, sequentially within the tick — this process never runs
two exports concurrently even if several tasks come due together, keeping behaviour simple to reason
about. A per-task exception is caught and recorded as that task's `Failed` outcome; it does not stop
the tick from moving on to the next due task (an unhandled exception escaping `DoWork` would, since the
tick engine logs and continues on the *next* timer tick, but would abandon whatever was left in this
one).

## Testing

Following this repo's no-mocking-library convention and the precedent set by
`ExportLoaderTaskRepositoryTests` (real Postgres via Testcontainers, not a faked `DbContext`) — the new
repository methods (`FindDueAsync`, `TryClaimAsync`, `ReapStaleInProgressAsync`,
`RecordOutcomeAsync`) are added to that same test class, against the same fixture:

- Two `TryClaimAsync` calls for the same row (simulating two replicas) — assert exactly one returns
  `true`.
- A task outside its `ToStartAtUtc`/`ToEndAtUtc` window is never returned by `FindDueAsync`.
- A task whose `LastStart + TriggerEvery` is still in the future is never returned by `FindDueAsync`.
- A stale `InProgress` row (`LastStart` older than the reap cutoff) is moved to `Failed` by
  `ReapStaleInProgressAsync`; a fresh `InProgress` row is left alone.

`ExportLoaderTaskScheduler` and the extended `ExportRunner` have no dedicated test project of their own
today (there is no `Abm.PD.Core.Application.Tests`) and both need a real database for the repository
calls anyway, so their tests are added to `Abm.PD.Core.Api.Tests` instead, resolving them from the
integration fixture's `IServiceProvider` — the same pattern the 2026-09-12 `ExportLoaderTask` design
established for repository access with no HTTP surface. Cases:

- End-to-end `DoWork` against a `Ready`, due task — `ExportRunner`'s FHIR-facing calls stubbed via the
  existing `StubHttpMessageHandler`/exporter harness — asserts `Completed`, `LastEnd` set, and
  `StateReason` reflects the load result.
- `DoWork` against a task whose exporter call throws — asserts `Failed`, `LastEnd` set, `StateReason`
  holds the exception message, and the tick still returns normally (doesn't propagate).
- `ExportRunner.Run(task, ct)` builds the `$export` `Parameters` correctly from `task.Parameter`
  (`Type`, `Since`, `TypeFilterList`), independent of the scheduler.

## Out of scope

- Per-task run-duration timeouts (a task's own export/load is not cancelled mid-flight by the
  scheduler; only the reaper — which acts on the DB row, not the running process — deals with a task
  that outlives `StaleInProgressAfter`).
- Manual/on-demand trigger of a task outside its schedule (e.g. an API endpoint to run one now) — not
  requested, not designed here.
- Retiring `Abm.PD.Console`/`ConsoleApplication`'s manual run path — left as-is; this design adds the
  scheduled path alongside it, doesn't remove the manual one.
- Lifting `Abm.Core.HostedService` out into a package shared between this repo and `PyroServer` —
  copying the folder is in scope; extracting a shared package is a separate decision for later.
