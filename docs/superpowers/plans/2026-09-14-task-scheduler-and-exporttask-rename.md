# Generic task scheduler, generic task repository, and ExportLoaderTask → ExportTask rename Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split scheduling (find-due/claim/reap/record-outcome) off `IExportLoaderTaskRepository` into a
new, entity-agnostic `ITaskRepository`/`TaskRepository` operating on `TaskBase`; rename
`ExportLoaderTaskScheduler` to `TaskScheduler`; then rename the `ExportLoaderTask` entity itself (and
everything downstream of it — repository, EF config, API endpoints/routes/contracts) to `ExportTask`.

**Architecture:** `TaskScheduler` calls `ITaskRepository` (generic, `TaskBase`-only, no navigations) to
find/claim/reap/record due tasks, then re-fetches the claimed row through the type-specific
`IExportTaskRepository.GetByIdAsync` (which `Include`s `DataSource`) before handing it to
`IExportRunner.Run` — the claimed `TaskBase` instance itself is never passed to `Run` directly, since it
never has `DataSource` populated. The rename is mechanical everywhere else: entity, repository,
EF configuration (including physical table/constraint names), API endpoint class/routes, and contracts.

**Tech Stack:** .NET (net10.0), EF Core / Npgsql, ASP.NET Core Minimal APIs, xunit, Testcontainers +
Respawn (`Abm.PD.Core.Api.Tests`), hand-rolled test doubles (no mocking library).

**Spec:** `docs/superpowers/specs/2026-09-14-task-scheduler-and-exporttask-rename-design.md`

## Global Constraints

- `Abm.PD.Core.Api` sets `TreatWarningsAsErrors` — a warning fails its build. `Abm.PD.Core.Domain`,
  `Abm.PD.Core.Repository`, `Abm.PD.Core.Application` do not.
- Australian English in comments/log messages ("organised", "initialise").
- File-scoped namespaces; primary constructors for DI; explicit types over `var`; multi-line method
  signatures, one parameter per line.
- No mocking library in any test project — hand-written test doubles only.
- `Abm.PD.Core.Api.Tests` integration tests need Docker running (Testcontainers spins up a real
  `postgres:17-alpine` per test run via `IntegrationTestFixture`).
- Never run `dotnet ef database update` or otherwise touch a real database — migration generation only
  (`dotnet ef migrations add`), which needs no live connection.
- Every commit message in this plan ends with the attribution lines from this session's system
  reminder (Co-Authored-By / Claude-Session) — copy them verbatim onto each commit made while executing
  this plan.

---

## Task 1: Fix the pre-existing build break and confirm a green baseline

There is an uncommitted, broken edit already sitting in the working tree:
`src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportLoaderTaskRepository.cs` has
`Task<IReadOnlyList<ExportLoaderTask>FindDueAsync(` (missing `>`) instead of
`Task<IReadOnlyList<ExportLoaderTask>> FindDueAsync(`. Nothing in this plan can be verified against a
broken build, so this is fixed first, in isolation, before any of the actual refactor begins.

**Files:**
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportLoaderTaskRepository.cs:130`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new — this task only restores the file to a compiling state matching its
  pre-existing (committed) shape.

- [ ] **Step 1: Fix the missing `>`**

In `src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportLoaderTaskRepository.cs`, find:

```csharp
    public async Task<IReadOnlyList<ExportLoaderTask>FindDueAsync(
        DateTime nowUtc,
        int failureAttemptCount,
        CancellationToken cancellationToken)
```

Replace with:

```csharp
    public async Task<IReadOnlyList<ExportLoaderTask>> FindDueAsync(
        DateTime nowUtc,
        int failureAttemptCount,
        CancellationToken cancellationToken)
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Run the full test suite to confirm a green baseline**

Run: `dotnet test src/Abm.PD/Abm.PD.slnx`
Expected: all tests pass (Docker must be running for `Abm.PD.Core.Api.Tests`'s Testcontainers-backed
integration tests). If anything is already red here, stop and investigate before proceeding — every
later task in this plan assumes this baseline was green.

- [ ] **Step 4: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportLoaderTaskRepository.cs
git commit -m "$(cat <<'EOF'
Fix uncommitted syntax error in ExportLoaderTaskRepository.FindDueAsync

Restores the missing closing angle bracket on the method's return type so the
solution builds again, ahead of the scheduler/repository refactor.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UMWQENpvJ4gbbbbtmhodF8
EOF
)"
```

---

## Task 2: Add generic `ITaskRepository` / `TaskRepository` (additive — nothing removed yet)

Add the new `TaskBase`-only scheduling repository alongside the existing
`IExportLoaderTaskRepository`, without touching the latter yet. This task is purely additive: a new
interface, a new implementation, a new `DbSet<TaskBase>`, and a new integration test file that proves
the four scheduling operations work identically to their existing `ExportLoaderTaskRepository`
counterparts, just against `TaskBase`. `ExportLoaderTaskScheduler` still uses the old repository at the
end of this task — it starts consuming `ITaskRepository` in Task 3.

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Domain/Repositories/ITaskRepository.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Domain/Enums/FailureCountUpdate.cs` (doc comment only)
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Repository/Repositories/TaskRepository.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs`
- Test: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskRepositoryTests.cs` (new)

**Interfaces:**
- Consumes: `TaskBase` (`src/Abm.PD/Abm.PD.Core.Domain/Entities/TaskBase.cs`, existing — `Id`, `State`,
  `StateReason`, `TriggerEvery`, `ToStartAtUtc`, `ToEndAtUtc`, `LastStart`, `LastEnd`, `FailureCount`),
  `TaskStateId`, `FailureCountUpdate` (existing enums), `ProviderDirectoryDbContext` (existing).
- Produces: `ITaskRepository` with `FindDueAsync(DateTime, int, CancellationToken) : Task<IReadOnlyList<TaskBase>>`,
  `TryClaimAsync(int, DateTime, CancellationToken) : Task<bool>`,
  `ReapStaleInProgressAsync(DateTime, CancellationToken) : Task`,
  `RecordOutcomeAsync(int, TaskStateId, DateTime, string?, FailureCountUpdate, CancellationToken) : Task`.
  `ProviderDirectoryDbContext.Tasks : DbSet<TaskBase>`. `TaskRepository : ITaskRepository`, DI-registered
  as scoped.

- [ ] **Step 1: Write the failing integration test**

Create `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskRepositoryTests.cs`:

```csharp
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class TaskRepositoryTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    private static ExportLoaderTask NewTask(
        string code,
        TaskStateId state = TaskStateId.Ready,
        DateTime? lastStart = null,
        TimeSpan? triggerEvery = null,
        DateTime? toStartAtUtc = null,
        DateTime? toEndAtUtc = null,
        int failureCount = 0)
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new ExportLoaderTask
        {
            Code = code,
            DisplayName = $"Task {code}",
            Description = null,
            State = state,
            StateReason = null,
            TriggerEvery = triggerEvery ?? TimeSpan.FromHours(24),
            ToStartAtUtc = toStartAtUtc,
            ToEndAtUtc = toEndAtUtc,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = lastStart,
            LastEnd = null,
            FailureCount = failureCount,
            DataSourceId = 0,
            DataSource = new DataSource { Code = Guid.NewGuid().ToString(), DisplayName = "Test Data Source" },
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    [Fact]
    public async Task FindDueAsync_TaskNeverRun_IsDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.Contains(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_LastStartPlusTriggerEveryInFuture_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), lastStart: DateTime.UtcNow, triggerEvery: TimeSpan.FromHours(1)),
            CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_BeforeToStartAtUtc_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        DateTime now = DateTime.UtcNow;
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), toStartAtUtc: now.AddDays(1)), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(now, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_AfterToEndAtUtc_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        DateTime now = DateTime.UtcNow;
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), toEndAtUtc: now.AddDays(-1)), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(now, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_InProgressTask_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_OnHoldTask_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.OnHold), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_FailedTaskExceedingFailureAttemptCount_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 4),
            CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_FailedTaskWithinFailureAttemptCount_IsDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 3),
            CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.Contains(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_CompletedTaskPastTriggerEvery_IsDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(
                Guid.NewGuid().ToString(),
                state: TaskStateId.Completed,
                lastStart: DateTime.UtcNow.AddHours(-2),
                triggerEvery: TimeSpan.FromHours(1)),
            CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.Contains(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_ZeroTriggerEvery_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), triggerEvery: TimeSpan.Zero), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task TryClaimAsync_ReadyTask_ClaimsAndSetsInProgressAndLastStart()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
        DateTime claimTime = new(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);

        bool claimed = await taskRepository.TryClaimAsync(added.Id, claimTime, CancellationToken.None);

        Assert.True(claimed);
        ExportLoaderTask? fetched = await exportLoaderTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
        Assert.Equal(claimTime, fetched.LastStart);
    }

    [Fact]
    public async Task TryClaimAsync_FailedTask_ClaimsAndSetsInProgress()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 1), CancellationToken.None);

        bool claimed = await taskRepository.TryClaimAsync(added.Id, DateTime.UtcNow, CancellationToken.None);

        Assert.True(claimed);
        ExportLoaderTask? fetched = await exportLoaderTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
    }

    [Fact]
    public async Task TryClaimAsync_AlreadyInProgressTask_ReturnsFalseAndLeavesLastStartUnchanged()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        DateTime originalLastStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress, lastStart: originalLastStart),
            CancellationToken.None);

        bool claimed = await taskRepository.TryClaimAsync(added.Id, DateTime.UtcNow, CancellationToken.None);

        Assert.False(claimed);
        ExportLoaderTask? fetched = await exportLoaderTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(originalLastStart, fetched!.LastStart);
    }

    [Fact]
    public async Task ReapStaleInProgressAsync_OlderThanCutoff_MovesToFailedWithReason()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        DateTime staleLastStart = DateTime.UtcNow.AddHours(-3);
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress, lastStart: staleLastStart),
            CancellationToken.None);

        await taskRepository.ReapStaleInProgressAsync(DateTime.UtcNow.AddHours(-2), CancellationToken.None);

        ExportLoaderTask? fetched = await exportLoaderTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.Failed, fetched!.State);
        Assert.Equal("Reaped: exceeded expected run duration", fetched.StateReason);
        Assert.Equal(1, fetched.FailureCount);
    }

    [Fact]
    public async Task ReapStaleInProgressAsync_NewerThanCutoff_IsLeftUnchanged()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        DateTime recentLastStart = DateTime.UtcNow.AddMinutes(-1);
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress, lastStart: recentLastStart),
            CancellationToken.None);

        await taskRepository.ReapStaleInProgressAsync(DateTime.UtcNow.AddHours(-2), CancellationToken.None);

        ExportLoaderTask? fetched = await exportLoaderTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
    }

    [Fact]
    public async Task RecordOutcomeAsync_SetsStateStateReasonAndLastEnd()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
        DateTime endTime = new(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);

        await taskRepository.RecordOutcomeAsync(
            added.Id, TaskStateId.Completed, endTime, "Committed 4 of 5, 1 failed", FailureCountUpdate.Unchanged, CancellationToken.None);

        ExportLoaderTask? fetched = await exportLoaderTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.Completed, fetched!.State);
        Assert.Equal("Committed 4 of 5, 1 failed", fetched.StateReason);
        Assert.Equal(endTime, fetched.LastEnd);
    }

    [Fact]
    public async Task RecordOutcomeAsync_ResetFailureCount_SetsFailureCountToZero()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 2), CancellationToken.None);

        await taskRepository.RecordOutcomeAsync(
            added.Id, TaskStateId.Completed, DateTime.UtcNow, "ok", FailureCountUpdate.Reset, CancellationToken.None);

        ExportLoaderTask? fetched = await exportLoaderTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(0, fetched!.FailureCount);
    }

    [Fact]
    public async Task RecordOutcomeAsync_IncrementFailureCount_AddsOneToFailureCount()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportLoaderTask added = await exportLoaderTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), failureCount: 1), CancellationToken.None);

        await taskRepository.RecordOutcomeAsync(
            added.Id, TaskStateId.Failed, DateTime.UtcNow, "boom", FailureCountUpdate.Increment, CancellationToken.None);

        ExportLoaderTask? fetched = await exportLoaderTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(2, fetched!.FailureCount);
    }
}
```

- [ ] **Step 2: Run it to confirm it fails to compile**

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests`
Expected: FAIL — `error CS0246: The type or namespace name 'ITaskRepository' could not be found`
(`ITaskRepository` doesn't exist yet).

- [ ] **Step 3: Create `ITaskRepository`**

Create `src/Abm.PD/Abm.PD.Core.Domain/Repositories/ITaskRepository.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Repositories;

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

- [ ] **Step 4: Update `FailureCountUpdate`'s doc comment**

In `src/Abm.PD/Abm.PD.Core.Domain/Enums/FailureCountUpdate.cs`, replace:

```csharp
/// <summary>
/// How IExportLoaderTaskRepository.RecordOutcomeAsync should treat TaskBase.FailureCount for the
/// outcome being recorded.
/// </summary>
```

with:

```csharp
/// <summary>
/// How ITaskRepository.RecordOutcomeAsync should treat TaskBase.FailureCount for the outcome being
/// recorded.
/// </summary>
```

- [ ] **Step 5: Add `Tasks` to `ProviderDirectoryDbContext`**

In `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`, replace:

```csharp
    public DbSet<ExportLoaderTask> ExportLoaderTasks => Set<ExportLoaderTask>();
```

with:

```csharp
    public DbSet<ExportLoaderTask> ExportLoaderTasks => Set<ExportLoaderTask>();
    public DbSet<TaskBase> Tasks => Set<TaskBase>();
```

- [ ] **Step 6: Create `TaskRepository`**

Create `src/Abm.PD/Abm.PD.Core.Repository/Repositories/TaskRepository.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository.Repositories;

public class TaskRepository(ProviderDirectoryDbContext dbContext) : ITaskRepository
{
    public async Task<IReadOnlyList<TaskBase>> FindDueAsync(
        DateTime nowUtc,
        int failureAttemptCount,
        CancellationToken cancellationToken)
    {
        return await dbContext.Tasks
            .Where(t => t.State == TaskStateId.Ready
                        || t.State == TaskStateId.Completed
                        || (t.State == TaskStateId.Failed && t.FailureCount <= failureAttemptCount))
            .Where(t => t.TriggerEvery > TimeSpan.Zero)
            .Where(t => t.ToStartAtUtc == null || t.ToStartAtUtc <= nowUtc)
            .Where(t => t.ToEndAtUtc == null || t.ToEndAtUtc >= nowUtc)
            .Where(t => t.LastStart == null || t.LastStart + t.TriggerEvery <= nowUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimAsync(
        int id,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        int rows = await dbContext.Tasks
            .Where(t => t.Id == id
                        && (t.State == TaskStateId.Ready
                            || t.State == TaskStateId.Completed
                            || t.State == TaskStateId.Failed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, TaskStateId.InProgress)
                .SetProperty(t => t.LastStart, nowUtc)
                .SetProperty(t => t.StateReason, (string?)null), cancellationToken);
        return rows == 1;
    }

    public async Task ReapStaleInProgressAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken)
    {
        await dbContext.Tasks
            .Where(t => t.State == TaskStateId.InProgress && t.LastStart != null && t.LastStart < olderThanUtc)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, TaskStateId.Failed)
                .SetProperty(t => t.StateReason, "Reaped: exceeded expected run duration")
                .SetProperty(t => t.FailureCount, t => t.FailureCount + 1), cancellationToken);
    }

    public async Task RecordOutcomeAsync(
        int id,
        TaskStateId state,
        DateTime nowUtc,
        string? stateReason,
        FailureCountUpdate failureCountUpdate,
        CancellationToken cancellationToken)
    {
        // ExecuteUpdateAsync takes an expression tree, so the FailureCount branch can't be factored
        // out into a shared block-bodied lambda - each outcome gets its own single, atomic UPDATE.
        switch (failureCountUpdate)
        {
            case FailureCountUpdate.Reset:
                await dbContext.Tasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEnd, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason)
                        .SetProperty(t => t.FailureCount, 0), cancellationToken);
                break;
            case FailureCountUpdate.Increment:
                await dbContext.Tasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEnd, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason)
                        .SetProperty(t => t.FailureCount, t => t.FailureCount + 1), cancellationToken);
                break;
            default:
                await dbContext.Tasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEnd, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason), cancellationToken);
                break;
        }
    }
}
```

- [ ] **Step 7: Register `ITaskRepository` in DI**

In `src/Abm.PD/Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs`, replace:

```csharp
        services.AddScoped<IResourceRepository, ResourceRepository>();
        services.AddScoped<IDataSourceRepository, DataSourceRepository>();
        services.AddScoped<IExportLoaderTaskRepository, ExportLoaderTaskRepository>();
        services.AddScoped<ISourceResourceRepository, SourceResourceRepository>();
```

with:

```csharp
        services.AddScoped<IResourceRepository, ResourceRepository>();
        services.AddScoped<IDataSourceRepository, DataSourceRepository>();
        services.AddScoped<IExportLoaderTaskRepository, ExportLoaderTaskRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ISourceResourceRepository, SourceResourceRepository>();
```

- [ ] **Step 8: Build and run the new test file**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: 0 errors.

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests --filter FullyQualifiedName~TaskRepositoryTests`
Expected: all 18 tests PASS.

- [ ] **Step 9: Run the full suite**

Run: `dotnet test src/Abm.PD/Abm.PD.slnx`
Expected: all tests pass (the old `ExportLoaderTaskRepositoryTests`'s scheduling tests still exist and
still pass too — they're removed in Task 3, not here).

- [ ] **Step 10: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Domain/Repositories/ITaskRepository.cs
git add src/Abm.PD/Abm.PD.Core.Domain/Enums/FailureCountUpdate.cs
git add src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs
git add src/Abm.PD/Abm.PD.Core.Repository/Repositories/TaskRepository.cs
git add src/Abm.PD/Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs
git add src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskRepositoryTests.cs
git commit -m "$(cat <<'EOF'
Add generic ITaskRepository/TaskRepository over TaskBase

Additive only - IExportLoaderTaskRepository is untouched and still owns the
same four scheduling methods too. TaskScheduler starts consuming the new
generic repository in the next commit.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UMWQENpvJ4gbbbbtmhodF8
EOF
)"
```

---

## Task 3: Switch `ExportLoaderTaskScheduler` to `ITaskRepository` + re-fetch; remove the old scheduling methods

`ExportLoaderTaskScheduler` starts calling `ITaskRepository` for scheduling and re-fetches the claimed
row through `IExportLoaderTaskRepository.GetByIdAsync` before calling `IExportRunner.Run` (see the
spec's "why the claimed instance can't be used directly" callout — `DataSource` is never populated on a
row from `ITaskRepository`). The four now-redundant methods are removed from `IExportLoaderTaskRepository`
/`ExportLoaderTaskRepository`, and their now-duplicated tests are removed from
`ExportLoaderTaskRepositoryTests.cs` (Task 2's `TaskRepositoryTests.cs` already covers that logic).

**Files:**
- Modify: `src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportLoaderTaskRepository.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportLoaderTaskRepository.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application/ExportLoaderTaskScheduler.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryTaskRepository.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryExportLoaderTaskRepository.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application.Tests/ExportLoaderTaskSchedulerTests.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskSchedulerTests.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs`
  (delete the now-redundant scheduling tests)

**Interfaces:**
- Consumes: `ITaskRepository` (Task 2). `IExportLoaderTaskRepository.GetByIdAsync` (existing, unchanged
  signature).
- Produces: `ExportLoaderTaskScheduler` now takes `(ITaskRepository, IExportLoaderTaskRepository,
  IServiceScopeFactory, IDateTimeProvider, IOptions<ExportLoaderTaskSchedulerSettings>,
  ILogger<ExportLoaderTaskScheduler>)`. `IExportLoaderTaskRepository` shrinks to `GetAllAsync`,
  `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `SearchAsync`.

- [ ] **Step 1: Write the new regression test for the re-fetch behaviour**

In `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskSchedulerTests.cs`, add this test
directly after `DoWork_DueReadyTask_ClaimsRunsAndRecordsCompleted`:

```csharp
    [Fact]
    public async Task DoWork_DueReadyTask_PassesFullyLoadedTaskWithDataSourceToRunner()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        ExportLoaderTaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(), CancellationToken.None);
        ExportLoaderTask? receivedTask = null;
        exportRunner.Behaviour = (task, _) =>
        {
            receivedTask = task;
            return Task.FromResult(new SourceResourceLoadResult(0, 0, 0, 0, []));
        };

        await scheduler.DoWork(CancellationToken.None);

        Assert.NotNull(receivedTask);
        Assert.NotNull(receivedTask!.DataSource);
        Assert.Equal(added.DataSourceId, receivedTask.DataSource.Id);
    }
```

- [ ] **Step 2: Run it to confirm it runs (locking in current behaviour before the repository swap)**

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests --filter FullyQualifiedName~DoWork_DueReadyTask_PassesFullyLoadedTaskWithDataSourceToRunner`
Expected: PASS even before Steps 3-5 — today's scheduler already gets `DataSource` via
`IExportLoaderTaskRepository.FindDueAsync`'s own `Include`. That's fine: the point of writing it now is
to have a permanent, named regression test in place *before* the repository swap in Steps 3-5, so it
actually exercises the new code path immediately afterwards rather than being added retroactively.

- [ ] **Step 3: Remove the four scheduling methods from `IExportLoaderTaskRepository`**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportLoaderTaskRepository.cs`
with:

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

- [ ] **Step 4: Remove the four scheduling method implementations from `ExportLoaderTaskRepository`**

In `src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportLoaderTaskRepository.cs`, delete everything from
the `FindDueAsync` method through the end of `RecordOutcomeAsync` (i.e. delete from the blank line after
`SearchAsync`'s closing `}` through the `RecordOutcomeAsync` method's closing `}`, keeping the file's
own final closing `}` for the class). Concretely, delete this entire block (it currently sits between
`SearchAsync` and the end of the class):

```csharp
    public async Task<IReadOnlyList<ExportLoaderTask>> FindDueAsync(
        DateTime nowUtc,
        int failureAttemptCount,
        CancellationToken cancellationToken)
    {
        return await dbContext.ExportLoaderTasks
            .Include(t => t.DataSource)
            .AsNoTracking()
            .Where(t => t.State == TaskStateId.Ready
                        || t.State == TaskStateId.Completed
                        || (t.State == TaskStateId.Failed && t.FailureCount <= failureAttemptCount))
            .Where(t => t.TriggerEvery > TimeSpan.Zero)
            .Where(t => t.ToStartAtUtc == null || t.ToStartAtUtc <= nowUtc)
            .Where(t => t.ToEndAtUtc == null || t.ToEndAtUtc >= nowUtc)
            .Where(t => t.LastStart == null || t.LastStart + t.TriggerEvery <= nowUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimAsync(
        int id,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        int rows = await dbContext.ExportLoaderTasks
            .Where(t => t.Id == id
                        && (t.State == TaskStateId.Ready
                            || t.State == TaskStateId.Completed
                            || t.State == TaskStateId.Failed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, TaskStateId.InProgress)
                .SetProperty(t => t.LastStart, nowUtc)
                .SetProperty(t => t.StateReason, (string?)null), cancellationToken);
        return rows == 1;
    }

    public async Task ReapStaleInProgressAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken)
    {
        await dbContext.ExportLoaderTasks
            .Where(t => t.State == TaskStateId.InProgress && t.LastStart != null && t.LastStart < olderThanUtc)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, TaskStateId.Failed)
                .SetProperty(t => t.StateReason, "Reaped: exceeded expected run duration")
                .SetProperty(t => t.FailureCount, t => t.FailureCount + 1), cancellationToken);
    }

    public async Task RecordOutcomeAsync(
        int id,
        TaskStateId state,
        DateTime nowUtc,
        string? stateReason,
        FailureCountUpdate failureCountUpdate,
        CancellationToken cancellationToken)
    {
        // ExecuteUpdateAsync takes an expression tree, so the FailureCount branch can't be factored
        // out into a shared block-bodied lambda - each outcome gets its own single, atomic UPDATE.
        switch (failureCountUpdate)
        {
            case FailureCountUpdate.Reset:
                await dbContext.ExportLoaderTasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEnd, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason)
                        .SetProperty(t => t.FailureCount, 0), cancellationToken);
                break;
            case FailureCountUpdate.Increment:
                await dbContext.ExportLoaderTasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEnd, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason)
                        .SetProperty(t => t.FailureCount, t => t.FailureCount + 1), cancellationToken);
                break;
            default:
                await dbContext.ExportLoaderTasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEnd, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason), cancellationToken);
                break;
        }
    }
```

- [ ] **Step 5: Rewrite `ExportLoaderTaskScheduler`**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Application/ExportLoaderTaskScheduler.cs` with:

```csharp
using Abm.Core.HostedService;
using Abm.Core.Time;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.Settings;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Abm.PD.Core.Application;

public class ExportLoaderTaskScheduler(
    ITaskRepository taskRepository,
    IExportLoaderTaskRepository exportLoaderTaskRepository,
    IServiceScopeFactory serviceScopeFactory,
    IDateTimeProvider dateTimeProvider,
    IOptions<ExportLoaderTaskSchedulerSettings> settings,
    ILogger<ExportLoaderTaskScheduler> logger) : ITimedHostedService
{
    public async Task DoWork(
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = dateTimeProvider.Now.UtcDateTime;

        await taskRepository.ReapStaleInProgressAsync(
            nowUtc - settings.Value.StaleInProgressAfter, cancellationToken);

        IReadOnlyList<TaskBase> dueTaskList = await taskRepository.FindDueAsync(
            nowUtc, settings.Value.FailureAttemptCount, cancellationToken);
        if (dueTaskList.Count == 0)
        {
            logger.LogInformation("{Service} for {Instance} found no tasks due to run", 
                nameof(ITimedHostedService), 
                nameof(ExportLoaderTaskScheduler));    
        }
        
        foreach (TaskBase task in dueTaskList)
        {
            if (!await taskRepository.TryClaimAsync(task.Id, nowUtc, cancellationToken))
            {
                // Another replica (or a human via the CRUD API) already claimed or changed this task
                // since FindDueAsync ran - this is the expected, silent outcome of losing the race.
                continue;
            }

            if (task is not ExportLoaderTask)
            {
                logger.LogWarning(
                    "Task {TaskCode} has unsupported {TypeId}, marking Failed", task.Code, task.TypeId);
                await taskRepository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Failed,
                    dateTimeProvider.Now.UtcDateTime,
                    $"Unsupported task type {task.TypeId}",
                    FailureCountUpdate.Increment,
                    CancellationToken.None);
                continue;
            }

            // Each task gets its own DI scope so its IExportRunner - and the scoped IFhirExporter/
            // IFhirBulkExporter underneath it - is a fresh instance. FhirBulkExporter is a stateful,
            // one-instance-one-export-session service; sharing one instance across every task in a tick
            // (as constructor injection into this class would do) made every task after the first fail
            // with "session already completed".
            using IServiceScope taskScope = serviceScopeFactory.CreateScope();
            IExportRunner exportRunner = taskScope.ServiceProvider.GetRequiredService<IExportRunner>();

            try
            {
                // task (from ITaskRepository) never has its DataSource navigation loaded - it's
                // re-fetched here through IExportLoaderTaskRepository, which Includes it, rather than
                // passed straight to IExportRunner.Run.
                ExportLoaderTask exportLoaderTask = await exportLoaderTaskRepository.GetByIdAsync(task.Id, cancellationToken)
                    ?? throw new InvalidOperationException($"ExportLoaderTask {task.Id} was claimed but no longer exists");

                SourceResourceLoadResult result = await exportRunner.Run(exportLoaderTask, cancellationToken);
                await taskRepository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Completed,
                    dateTimeProvider.Now.UtcDateTime,
                    $"Persisted {result.CommittedCount} of {result.SubmittedCount}, {result.FailedCount} failed",
                    FailureCountUpdate.Reset,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "ExportLoaderTask {TaskCode} failed", task.Code);
                await taskRepository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Failed,
                    dateTimeProvider.Now.UtcDateTime,
                    exception.Message,
                    FailureCountUpdate.Increment,
                    CancellationToken.None);
            }
        }
    }
}
```

- [ ] **Step 6: Create `InMemoryTaskRepository`**

Create `src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryTaskRepository.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// Hand rolled, in-memory fake for ExportLoaderTaskScheduler tests that need a real (if simplistic)
// FindDueAsync/TryClaimAsync/RecordOutcomeAsync/ReapStaleInProgressAsync - no claim atomicity is
// modelled, this is single-threaded test code driving the scheduler directly.
public sealed class InMemoryTaskRepository(List<TaskBase> tasks) : ITaskRepository
{
    public Task<IReadOnlyList<TaskBase>> FindDueAsync(
        DateTime nowUtc,
        int failureAttemptCount,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TaskBase> due = tasks
            .Where(t => t.State == TaskStateId.Ready
                        || t.State == TaskStateId.Completed
                        || (t.State == TaskStateId.Failed && t.FailureCount <= failureAttemptCount))
            .Where(t => t.TriggerEvery > TimeSpan.Zero)
            .Where(t => t.ToStartAtUtc == null || t.ToStartAtUtc <= nowUtc)
            .Where(t => t.ToEndAtUtc == null || t.ToEndAtUtc >= nowUtc)
            .Where(t => t.LastStart == null || t.LastStart + t.TriggerEvery <= nowUtc)
            .ToList();
        return Task.FromResult(due);
    }

    public Task<bool> TryClaimAsync(
        int id,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        TaskBase? task = tasks.SingleOrDefault(t => t.Id == id);
        if (task is null || task.State == TaskStateId.InProgress)
        {
            return Task.FromResult(false);
        }

        task.State = TaskStateId.InProgress;
        task.LastStart = nowUtc;
        task.StateReason = null;
        return Task.FromResult(true);
    }

    public Task ReapStaleInProgressAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken)
    {
        foreach (TaskBase task in tasks.Where(
                     t => t.State == TaskStateId.InProgress && t.LastStart != null && t.LastStart < olderThanUtc))
        {
            task.State = TaskStateId.Failed;
            task.StateReason = "Reaped: exceeded expected run duration";
            task.FailureCount++;
        }

        return Task.CompletedTask;
    }

    public Task RecordOutcomeAsync(
        int id,
        TaskStateId state,
        DateTime nowUtc,
        string? stateReason,
        FailureCountUpdate failureCountUpdate,
        CancellationToken cancellationToken)
    {
        TaskBase? task = tasks.SingleOrDefault(t => t.Id == id);
        if (task is not null)
        {
            task.State = state;
            task.LastEnd = nowUtc;
            task.StateReason = stateReason;
            task.FailureCount = failureCountUpdate switch
            {
                FailureCountUpdate.Reset => 0,
                FailureCountUpdate.Increment => task.FailureCount + 1,
                _ => task.FailureCount
            };
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 7: Shrink `InMemoryExportLoaderTaskRepository` to a working `GetByIdAsync` plus stubs**

Replace the full contents of
`src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryExportLoaderTaskRepository.cs` with:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// ExportLoaderTaskScheduler re-fetches the fully-loaded ExportLoaderTask by Id after claiming it (see
// the design spec's "why the claimed instance can't be used directly" callout) - GetByIdAsync must
// actually work for that flow to be exercised in these tests, unlike the other CRUD members, which
// nothing here calls.
public sealed class InMemoryExportLoaderTaskRepository(List<ExportLoaderTask> tasks) : IExportLoaderTaskRepository
{
    public Task<IReadOnlyList<ExportLoaderTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ExportLoaderTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(tasks.SingleOrDefault(t => t.Id == id));
    }

    public Task<ExportLoaderTask> AddAsync(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ExportLoaderTask?> UpdateAsync(
        int id,
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<ExportLoaderTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
```

- [ ] **Step 8: Wire both fakes into `ExportLoaderTaskSchedulerTests` (Application.Tests)**

Replace the full contents of
`src/Abm.PD/Abm.PD.Core.Application.Tests/ExportLoaderTaskSchedulerTests.cs` with:

```csharp
using Abm.Core.Time;
using Abm.PD.Core.Application.Settings;
using Abm.PD.Core.Application.Tests.TestDoubles;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Tests;

public class ExportLoaderTaskSchedulerTests
{
    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset Now => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static ExportLoaderTask NewTask(
        int id,
        string code,
        TaskStateId state = TaskStateId.Ready,
        int failureCount = 0)
    {
        return new ExportLoaderTask
        {
            Id = id,
            Code = code,
            DisplayName = $"Task {code}",
            Description = null,
            State = state,
            StateReason = null,
            TriggerEvery = TimeSpan.FromHours(24),
            ToStartAtUtc = null,
            ToEndAtUtc = null,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            LastStart = null,
            LastEnd = null,
            FailureCount = failureCount,
            DataSourceId = 1,
            DataSource = new DataSource { Id = 1, Code = "test-data-source", DisplayName = "Test Data Source" },
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    private static ServiceProvider BuildProvider(
        List<ExportLoaderTask> seededTasks,
        IExportRunner exportRunner,
        int failureAttemptCount = 3)
    {
        ServiceCollection services = new();
        services.AddSingleton<IExportRunner>(exportRunner);
        services.AddSingleton<ITaskRepository>(new InMemoryTaskRepository(seededTasks.Cast<TaskBase>().ToList()));
        services.AddSingleton<IExportLoaderTaskRepository>(new InMemoryExportLoaderTaskRepository(seededTasks));
        services.AddSingleton<IDateTimeProvider>(new FixedDateTimeProvider());
        services.AddSingleton<IOptions<ExportLoaderTaskSchedulerSettings>>(
            Options.Create(new ExportLoaderTaskSchedulerSettings { FailureAttemptCount = failureAttemptCount }));
        services.AddSingleton<ILogger<ExportLoaderTaskScheduler>>(NullLogger<ExportLoaderTaskScheduler>.Instance);
        services.AddScoped<ExportLoaderTaskScheduler>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DoWork_TwoDueTasks_EachGetsItsOwnExportRunnerInstance()
    {
        List<(int TaskId, Guid InstanceId)> calls = [];
        List<ExportLoaderTask> seededTasks = [NewTask(1, "task-one"), NewTask(2, "task-two")];

        ServiceCollection services = new();
        services.AddSingleton(calls);
        services.AddScoped<IExportRunner, ScopeTrackingExportRunner>();
        services.AddSingleton<ITaskRepository>(new InMemoryTaskRepository(seededTasks.Cast<TaskBase>().ToList()));
        services.AddSingleton<IExportLoaderTaskRepository>(new InMemoryExportLoaderTaskRepository(seededTasks));
        services.AddSingleton<IDateTimeProvider>(new FixedDateTimeProvider());
        services.AddSingleton<IOptions<ExportLoaderTaskSchedulerSettings>>(
            Options.Create(new ExportLoaderTaskSchedulerSettings()));
        services.AddSingleton<ILogger<ExportLoaderTaskScheduler>>(NullLogger<ExportLoaderTaskScheduler>.Instance);
        services.AddScoped<ExportLoaderTaskScheduler>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        // Simulates the tick engine's own per-tick outer scope - the scheduler itself is resolved
        // once per tick, exactly as ITimedHostedService driving it would do.
        using IServiceScope tickScope = provider.CreateScope();
        ExportLoaderTaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Equal(2, calls.Count);
        Assert.NotEqual(calls[0].InstanceId, calls[1].InstanceId);
    }

    [Fact]
    public async Task DoWork_RunnerThrows_IncrementsFailureCountAndSetsFailed()
    {
        List<ExportLoaderTask> seededTasks = [NewTask(1, "task-one")];
        await using ServiceProvider provider = BuildProvider(seededTasks, new ThrowingExportRunner());
        using IServiceScope tickScope = provider.CreateScope();
        ExportLoaderTaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Equal(TaskStateId.Failed, seededTasks[0].State);
        Assert.Equal(1, seededTasks[0].FailureCount);
    }

    [Fact]
    public async Task DoWork_RunnerSucceeds_ResetsFailureCountToZero()
    {
        List<ExportLoaderTask> seededTasks = [NewTask(1, "task-one", failureCount: 2)];
        await using ServiceProvider provider = BuildProvider(seededTasks, new ScopeTrackingExportRunner([]));
        using IServiceScope tickScope = provider.CreateScope();
        ExportLoaderTaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Equal(TaskStateId.Completed, seededTasks[0].State);
        Assert.Equal(0, seededTasks[0].FailureCount);
    }

    [Fact]
    public async Task DoWork_FailedTaskWithinFailureAttemptCount_IsRun()
    {
        List<(int TaskId, Guid InstanceId)> calls = [];
        List<ExportLoaderTask> seededTasks =
            [NewTask(1, "task-one", state: TaskStateId.Failed, failureCount: 3)];
        await using ServiceProvider provider = BuildProvider(
            seededTasks, new ScopeTrackingExportRunner(calls), failureAttemptCount: 3);
        using IServiceScope tickScope = provider.CreateScope();
        ExportLoaderTaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Single(calls);
    }

    [Fact]
    public async Task DoWork_FailedTaskExceedingFailureAttemptCount_IsNotRun()
    {
        List<(int TaskId, Guid InstanceId)> calls = [];
        List<ExportLoaderTask> seededTasks =
            [NewTask(1, "task-one", state: TaskStateId.Failed, failureCount: 4)];
        await using ServiceProvider provider = BuildProvider(
            seededTasks, new ScopeTrackingExportRunner(calls), failureAttemptCount: 3);
        using IServiceScope tickScope = provider.CreateScope();
        ExportLoaderTaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Empty(calls);
    }
}
```

- [ ] **Step 9: Delete the now-redundant scheduling tests from `ExportLoaderTaskRepositoryTests.cs`**

In `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs`, delete every
`[Fact]` method from `FindDueAsync_TaskNeverRun_IsDue` through
`RecordOutcomeAsync_IncrementFailureCount_AddsOneToFailureCount` inclusive (this is everything between
`SearchAsync_ByLastStartRange_FindsOnlyTasksWithinRange`'s closing `}` and the class's own final closing
`}`) — all 18 of them, now covered by `TaskRepositoryTests.cs` from Task 2. The file should end with
`SearchAsync_ByLastStartRange_FindsOnlyTasksWithinRange`'s closing brace, a blank line, then the class's
closing `}`.

- [ ] **Step 10: Build and test**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: 0 errors.

Run: `dotnet test src/Abm.PD/Abm.PD.slnx`
Expected: all tests pass, including the new
`DoWork_DueReadyTask_PassesFullyLoadedTaskWithDataSourceToRunner` from Step 1.

- [ ] **Step 11: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportLoaderTaskRepository.cs
git add src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportLoaderTaskRepository.cs
git add src/Abm.PD/Abm.PD.Core.Application/ExportLoaderTaskScheduler.cs
git add src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryTaskRepository.cs
git add src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryExportLoaderTaskRepository.cs
git add src/Abm.PD/Abm.PD.Core.Application.Tests/ExportLoaderTaskSchedulerTests.cs
git add src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskSchedulerTests.cs
git add src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs
git commit -m "$(cat <<'EOF'
Switch ExportLoaderTaskScheduler to ITaskRepository, re-fetch before running

The scheduler's find-due/claim/reap/record-outcome calls move onto the
generic ITaskRepository. The claimed TaskBase never has DataSource loaded, so
the scheduler re-fetches through IExportLoaderTaskRepository.GetByIdAsync
before calling IExportRunner.Run. Removes the four now-redundant scheduling
methods from IExportLoaderTaskRepository/ExportLoaderTaskRepository and their
now-duplicated tests.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UMWQENpvJ4gbbbbtmhodF8
EOF
)"
```

---

## Task 4: Rename `ExportLoaderTaskScheduler` → `TaskScheduler`, settings, and fix the naming collision

Pure rename of the scheduler class and its settings record, now that its behaviour has stabilised.
Includes the one file where `TaskScheduler` collides with `System.Threading.Tasks.TaskScheduler` (see
the spec's collision note): `Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskSchedulerTests.cs`
(namespace `Abm.PD.Core.Api.Tests.ExportLoaderTasks`, outside the `Abm.PD.Core.Application` namespace
chain, with an explicit `using Abm.PD.Core.Application;`).

**Files:**
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Application/TaskScheduler.cs` (was `ExportLoaderTaskScheduler.cs`)
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Application/Settings/TaskSchedulerSettings.cs` (was `ExportLoaderTaskSchedulerSettings.cs`)
- Modify: `src/Abm.PD/Abm.PD.Core.Application/DependencyInjection/ServiceCollectionExtension.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Api/appsettings.json`
- Modify: `src/Abm.PD/Abm.PD.Core.Api/appsettings.Development.json`
- Modify: `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/CoreApiWebApplicationFactory.cs`
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Application.Tests/TaskSchedulerTests.cs` (was `ExportLoaderTaskSchedulerTests.cs`)
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskSchedulerTests.cs` (was `ExportLoaderTaskSchedulerTests.cs`)

**Interfaces:**
- Consumes: `ITaskRepository`, `IExportLoaderTaskRepository` (Tasks 2-3, unchanged signatures).
- Produces: `TaskScheduler` (renamed from `ExportLoaderTaskScheduler`, same constructor shape but
  `IOptions<TaskSchedulerSettings>` and `ILogger<TaskScheduler>`). `TaskSchedulerSettings` (renamed from
  `ExportLoaderTaskSchedulerSettings`), `SectionName = "TaskScheduler"`.

- [ ] **Step 1: Rename the settings record**

Delete `src/Abm.PD/Abm.PD.Core.Application/Settings/ExportLoaderTaskSchedulerSettings.cs`. Create
`src/Abm.PD/Abm.PD.Core.Application/Settings/TaskSchedulerSettings.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Abm.PD.Core.Application.Settings;

public record TaskSchedulerSettings
{
    public const string SectionName = "TaskScheduler";

    /// <summary>
    /// How often the scheduler checks for due tasks. Independent of any task's own TriggerEvery - this
    /// is the poll granularity, not a schedule.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "23:59:59")]
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a task may sit InProgress before the scheduler assumes its runner crashed or was
    /// killed mid-run (no clean Completed/Failed update ever arrived) and reaps it back to Failed.
    /// Must comfortably exceed the slowest real export/load run, or a live task gets reaped out from
    /// under itself.
    /// </summary>
    [Range(typeof(TimeSpan), "00:05:00", "24:00:00")]
    public TimeSpan StaleInProgressAfter { get; init; } = TimeSpan.FromHours(2);

    /// <summary>
    /// How many consecutive failures a Failed task may accumulate and still be found Due. A Failed
    /// task is retried while its FailureCount is less than or equal to this value - the default of 3
    /// allows up to 4 attempts in total before the task stops being picked up.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int FailureAttemptCount { get; init; } = 3;
}
```

- [ ] **Step 2: Rename the scheduler class**

Delete `src/Abm.PD/Abm.PD.Core.Application/ExportLoaderTaskScheduler.cs`. Create
`src/Abm.PD/Abm.PD.Core.Application/TaskScheduler.cs` with the same body as Task 3's rewrite, renaming
the class, its settings type, and its logger type:

```csharp
using Abm.Core.HostedService;
using Abm.Core.Time;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.Settings;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Abm.PD.Core.Application;

public class TaskScheduler(
    ITaskRepository taskRepository,
    IExportLoaderTaskRepository exportLoaderTaskRepository,
    IServiceScopeFactory serviceScopeFactory,
    IDateTimeProvider dateTimeProvider,
    IOptions<TaskSchedulerSettings> settings,
    ILogger<TaskScheduler> logger) : ITimedHostedService
{
    public async Task DoWork(
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = dateTimeProvider.Now.UtcDateTime;

        await taskRepository.ReapStaleInProgressAsync(
            nowUtc - settings.Value.StaleInProgressAfter, cancellationToken);

        IReadOnlyList<TaskBase> dueTaskList = await taskRepository.FindDueAsync(
            nowUtc, settings.Value.FailureAttemptCount, cancellationToken);
        if (dueTaskList.Count == 0)
        {
            logger.LogInformation("{Service} for {Instance} found no tasks due to run", 
                nameof(ITimedHostedService), 
                nameof(TaskScheduler));    
        }
        
        foreach (TaskBase task in dueTaskList)
        {
            if (!await taskRepository.TryClaimAsync(task.Id, nowUtc, cancellationToken))
            {
                // Another replica (or a human via the CRUD API) already claimed or changed this task
                // since FindDueAsync ran - this is the expected, silent outcome of losing the race.
                continue;
            }

            if (task is not ExportLoaderTask)
            {
                logger.LogWarning(
                    "Task {TaskCode} has unsupported {TypeId}, marking Failed", task.Code, task.TypeId);
                await taskRepository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Failed,
                    dateTimeProvider.Now.UtcDateTime,
                    $"Unsupported task type {task.TypeId}",
                    FailureCountUpdate.Increment,
                    CancellationToken.None);
                continue;
            }

            // Each task gets its own DI scope so its IExportRunner - and the scoped IFhirExporter/
            // IFhirBulkExporter underneath it - is a fresh instance. FhirBulkExporter is a stateful,
            // one-instance-one-export-session service; sharing one instance across every task in a tick
            // (as constructor injection into this class would do) made every task after the first fail
            // with "session already completed".
            using IServiceScope taskScope = serviceScopeFactory.CreateScope();
            IExportRunner exportRunner = taskScope.ServiceProvider.GetRequiredService<IExportRunner>();

            try
            {
                // task (from ITaskRepository) never has its DataSource navigation loaded - it's
                // re-fetched here through IExportLoaderTaskRepository, which Includes it, rather than
                // passed straight to IExportRunner.Run.
                ExportLoaderTask exportLoaderTask = await exportLoaderTaskRepository.GetByIdAsync(task.Id, cancellationToken)
                    ?? throw new InvalidOperationException($"ExportLoaderTask {task.Id} was claimed but no longer exists");

                SourceResourceLoadResult result = await exportRunner.Run(exportLoaderTask, cancellationToken);
                await taskRepository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Completed,
                    dateTimeProvider.Now.UtcDateTime,
                    $"Persisted {result.CommittedCount} of {result.SubmittedCount}, {result.FailedCount} failed",
                    FailureCountUpdate.Reset,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "ExportLoaderTask {TaskCode} failed", task.Code);
                await taskRepository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Failed,
                    dateTimeProvider.Now.UtcDateTime,
                    exception.Message,
                    FailureCountUpdate.Increment,
                    CancellationToken.None);
            }
        }
    }
}
```

- [ ] **Step 3: Update the Application DI extension**

Replace the full contents of
`src/Abm.PD/Abm.PD.Core.Application/DependencyInjection/ServiceCollectionExtension.cs` with:

```csharp
using Abm.Core.HostedService;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Application.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddCoreProviderDirectoryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<TaskSchedulerSettings>()
            .Bind(configuration.GetSection(TaskSchedulerSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SourceResourceLoaderSettings>()
            .Bind(configuration.GetSection(SourceResourceLoaderSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // AddTimedHostedService<T>'s configurator runs synchronously at registration time, before the
        // host is built, so it cannot resolve IOptions<T> from the container the way the settings
        // above are read once the app starts - PollInterval is read straight off configuration here
        // instead, landing on the same bound value either way.
        TaskSchedulerSettings schedulerSettings = configuration
            .GetSection(TaskSchedulerSettings.SectionName)
            .Get<TaskSchedulerSettings>() ?? new TaskSchedulerSettings();

        services.AddScoped<IExportRunner, ExportRunner>();
        services.AddScoped<ISourceResourceLoader, SourceResourceLoader>();

        // AddTimedHostedService<T> already registers T (TaskScheduler) as Scoped and adds
        // the IHostedService that ticks it - no separate AddScoped<TaskScheduler>() call.
        services.AddTimedHostedService<TaskScheduler>(opt =>
        {
            opt.TriggersEvery = schedulerSettings.PollInterval;
        });

        return services;
    }
}
```

- [ ] **Step 4: Rename the config section in both appsettings files**

In `src/Abm.PD/Abm.PD.Core.Api/appsettings.json`, replace:

```json
  "ExportLoaderTaskScheduler": {
    "PollInterval": "00:01:00",
    "StaleInProgressAfter": "01:00:00",
    "FailureAttemptCount" : 3
  },
```

with:

```json
  "TaskScheduler": {
    "PollInterval": "00:01:00",
    "StaleInProgressAfter": "01:00:00",
    "FailureAttemptCount" : 3
  },
```

In `src/Abm.PD/Abm.PD.Core.Api/appsettings.Development.json`, replace:

```json
  "ExportLoaderTaskScheduler" :
  {
    "PollInterval" : "00:00:30",
    "StaleInProgressAfter":"01:00:00",
    "FailureAttemptCount" : 3
  },
```

with:

```json
  "TaskScheduler" :
  {
    "PollInterval" : "00:00:30",
    "StaleInProgressAfter":"01:00:00",
    "FailureAttemptCount" : 3
  },
```

- [ ] **Step 5: Update the integration test factory's config keys**

In `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/CoreApiWebApplicationFactory.cs`, replace:

```csharp
                // The longest PollInterval ExportLoaderTaskSchedulerSettings' own validation allows
                // (00:10:00) - still vastly longer than any test run, so the scheduler's own background
                // timer never ticks during a test run. IntegrationTestFixture builds one factory for the
                // whole test collection's lifetime, so without this the real 30-second production default
                // would keep firing in the background across every other test in the suite.
                ["ExportLoaderTaskScheduler:PollInterval"] = "00:10:00",
                // The minimum this repo's settings validation allows - short enough that a reaped-task
                // test only needs a LastStart a few minutes in the past, not the 2-hour production default.
                ["ExportLoaderTaskScheduler:StaleInProgressAfter"] = "00:05:00",
```

with:

```csharp
                // The longest PollInterval TaskSchedulerSettings' own validation allows
                // (00:10:00) - still vastly longer than any test run, so the scheduler's own background
                // timer never ticks during a test run. IntegrationTestFixture builds one factory for the
                // whole test collection's lifetime, so without this the real 30-second production default
                // would keep firing in the background across every other test in the suite.
                ["TaskScheduler:PollInterval"] = "00:10:00",
                // The minimum this repo's settings validation allows - short enough that a reaped-task
                // test only needs a LastStart a few minutes in the past, not the 2-hour production default.
                ["TaskScheduler:StaleInProgressAfter"] = "00:05:00",
```

Also update the doc comment referencing the old class name, replacing:

```csharp
/// so ExportLoaderTaskScheduler never makes a real FHIR HTTP call during any Abm.PD.Core.Api.Tests run.
```

with:

```csharp
/// so TaskScheduler never makes a real FHIR HTTP call during any Abm.PD.Core.Api.Tests run.
```

(that line is in `src/Abm.PD/Abm.PD.Core.Api.Tests/TestDoubles/ConfigurableExportRunner.cs`, not
`CoreApiWebApplicationFactory.cs` — update it there).

- [ ] **Step 6: Rename the Application.Tests scheduler test file**

Delete `src/Abm.PD/Abm.PD.Core.Application.Tests/ExportLoaderTaskSchedulerTests.cs`. Create
`src/Abm.PD/Abm.PD.Core.Application.Tests/TaskSchedulerTests.cs` with the same content as Task 3's
rewrite of that file, renaming the class and every `ExportLoaderTaskScheduler`/
`ExportLoaderTaskSchedulerSettings` reference to `TaskScheduler`/`TaskSchedulerSettings` (the type
resolves unqualified here — this namespace, `Abm.PD.Core.Application.Tests`, is a dotted descendant of
`Abm.PD.Core.Application`):

```csharp
using Abm.Core.Time;
using Abm.PD.Core.Application.Settings;
using Abm.PD.Core.Application.Tests.TestDoubles;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Tests;

public class TaskSchedulerTests
{
    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset Now => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static ExportLoaderTask NewTask(
        int id,
        string code,
        TaskStateId state = TaskStateId.Ready,
        int failureCount = 0)
    {
        return new ExportLoaderTask
        {
            Id = id,
            Code = code,
            DisplayName = $"Task {code}",
            Description = null,
            State = state,
            StateReason = null,
            TriggerEvery = TimeSpan.FromHours(24),
            ToStartAtUtc = null,
            ToEndAtUtc = null,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            LastStart = null,
            LastEnd = null,
            FailureCount = failureCount,
            DataSourceId = 1,
            DataSource = new DataSource { Id = 1, Code = "test-data-source", DisplayName = "Test Data Source" },
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    private static ServiceProvider BuildProvider(
        List<ExportLoaderTask> seededTasks,
        IExportRunner exportRunner,
        int failureAttemptCount = 3)
    {
        ServiceCollection services = new();
        services.AddSingleton<IExportRunner>(exportRunner);
        services.AddSingleton<ITaskRepository>(new InMemoryTaskRepository(seededTasks.Cast<TaskBase>().ToList()));
        services.AddSingleton<IExportLoaderTaskRepository>(new InMemoryExportLoaderTaskRepository(seededTasks));
        services.AddSingleton<IDateTimeProvider>(new FixedDateTimeProvider());
        services.AddSingleton<IOptions<TaskSchedulerSettings>>(
            Options.Create(new TaskSchedulerSettings { FailureAttemptCount = failureAttemptCount }));
        services.AddSingleton<ILogger<TaskScheduler>>(NullLogger<TaskScheduler>.Instance);
        services.AddScoped<TaskScheduler>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DoWork_TwoDueTasks_EachGetsItsOwnExportRunnerInstance()
    {
        List<(int TaskId, Guid InstanceId)> calls = [];
        List<ExportLoaderTask> seededTasks = [NewTask(1, "task-one"), NewTask(2, "task-two")];

        ServiceCollection services = new();
        services.AddSingleton(calls);
        services.AddScoped<IExportRunner, ScopeTrackingExportRunner>();
        services.AddSingleton<ITaskRepository>(new InMemoryTaskRepository(seededTasks.Cast<TaskBase>().ToList()));
        services.AddSingleton<IExportLoaderTaskRepository>(new InMemoryExportLoaderTaskRepository(seededTasks));
        services.AddSingleton<IDateTimeProvider>(new FixedDateTimeProvider());
        services.AddSingleton<IOptions<TaskSchedulerSettings>>(
            Options.Create(new TaskSchedulerSettings()));
        services.AddSingleton<ILogger<TaskScheduler>>(NullLogger<TaskScheduler>.Instance);
        services.AddScoped<TaskScheduler>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        // Simulates the tick engine's own per-tick outer scope - the scheduler itself is resolved
        // once per tick, exactly as ITimedHostedService driving it would do.
        using IServiceScope tickScope = provider.CreateScope();
        TaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<TaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Equal(2, calls.Count);
        Assert.NotEqual(calls[0].InstanceId, calls[1].InstanceId);
    }

    [Fact]
    public async Task DoWork_RunnerThrows_IncrementsFailureCountAndSetsFailed()
    {
        List<ExportLoaderTask> seededTasks = [NewTask(1, "task-one")];
        await using ServiceProvider provider = BuildProvider(seededTasks, new ThrowingExportRunner());
        using IServiceScope tickScope = provider.CreateScope();
        TaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<TaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Equal(TaskStateId.Failed, seededTasks[0].State);
        Assert.Equal(1, seededTasks[0].FailureCount);
    }

    [Fact]
    public async Task DoWork_RunnerSucceeds_ResetsFailureCountToZero()
    {
        List<ExportLoaderTask> seededTasks = [NewTask(1, "task-one", failureCount: 2)];
        await using ServiceProvider provider = BuildProvider(seededTasks, new ScopeTrackingExportRunner([]));
        using IServiceScope tickScope = provider.CreateScope();
        TaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<TaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Equal(TaskStateId.Completed, seededTasks[0].State);
        Assert.Equal(0, seededTasks[0].FailureCount);
    }

    [Fact]
    public async Task DoWork_FailedTaskWithinFailureAttemptCount_IsRun()
    {
        List<(int TaskId, Guid InstanceId)> calls = [];
        List<ExportLoaderTask> seededTasks =
            [NewTask(1, "task-one", state: TaskStateId.Failed, failureCount: 3)];
        await using ServiceProvider provider = BuildProvider(
            seededTasks, new ScopeTrackingExportRunner(calls), failureAttemptCount: 3);
        using IServiceScope tickScope = provider.CreateScope();
        TaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<TaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Single(calls);
    }

    [Fact]
    public async Task DoWork_FailedTaskExceedingFailureAttemptCount_IsNotRun()
    {
        List<(int TaskId, Guid InstanceId)> calls = [];
        List<ExportLoaderTask> seededTasks =
            [NewTask(1, "task-one", state: TaskStateId.Failed, failureCount: 4)];
        await using ServiceProvider provider = BuildProvider(
            seededTasks, new ScopeTrackingExportRunner(calls), failureAttemptCount: 3);
        using IServiceScope tickScope = provider.CreateScope();
        TaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<TaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Empty(calls);
    }
}
```

- [ ] **Step 7: Rename the Api.Tests scheduler test file, fully-qualifying `TaskScheduler`**

Delete `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskSchedulerTests.cs`. Create
`src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskSchedulerTests.cs`. This file's namespace,
`Abm.PD.Core.Api.Tests.ExportLoaderTasks`, is **not** a dotted descendant of `Abm.PD.Core.Application`,
so — per the spec's collision note — every reference to the scheduler type must be fully qualified as
`Abm.PD.Core.Application.TaskScheduler` rather than bare `TaskScheduler`:

```csharp
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Api.Tests.TestDoubles;
using Abm.PD.Core.Application;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class TaskSchedulerTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    private static ExportLoaderTask NewTask(
        TaskStateId state = TaskStateId.Ready,
        DateTime? lastStart = null,
        TimeSpan? triggerEvery = null,
        int failureCount = 0)
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new ExportLoaderTask
        {
            Code = Guid.NewGuid().ToString(),
            DisplayName = "Scheduler test task",
            Description = null,
            State = state,
            StateReason = null,
            TriggerEvery = triggerEvery ?? TimeSpan.FromHours(24),
            ToStartAtUtc = null,
            ToEndAtUtc = null,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = lastStart,
            LastEnd = null,
            FailureCount = failureCount,
            DataSourceId = 0,
            DataSource = new DataSource { Code = Guid.NewGuid().ToString(), DisplayName = "Test Data Source" },
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    [Fact]
    public async Task DoWork_DueReadyTask_ClaimsRunsAndRecordsCompleted()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(), CancellationToken.None);
        exportRunner.Behaviour = (_, _) => Task.FromResult(
            new SourceResourceLoadResult(SubmittedCount: 5, CommittedCount: 4, FailedCount: 1, BatchCount: 1, RetainedFailures: []));

        await scheduler.DoWork(CancellationToken.None);

        ExportLoaderTask? updated = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(TaskStateId.Completed, updated!.State);
        Assert.NotNull(updated.LastEnd);
        Assert.Equal("Persisted 4 of 5, 1 failed", updated.StateReason);
    }

    [Fact]
    public async Task DoWork_DueReadyTask_PassesFullyLoadedTaskWithDataSourceToRunner()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(), CancellationToken.None);
        ExportLoaderTask? receivedTask = null;
        exportRunner.Behaviour = (task, _) =>
        {
            receivedTask = task;
            return Task.FromResult(new SourceResourceLoadResult(0, 0, 0, 0, []));
        };

        await scheduler.DoWork(CancellationToken.None);

        Assert.NotNull(receivedTask);
        Assert.NotNull(receivedTask!.DataSource);
        Assert.Equal(added.DataSourceId, receivedTask.DataSource.Id);
    }

    [Fact]
    public async Task DoWork_RunnerThrows_RecordsFailedWithExceptionMessage()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(), CancellationToken.None);
        exportRunner.Behaviour = (_, _) => throw new InvalidOperationException("SIT server unreachable");

        await scheduler.DoWork(CancellationToken.None);

        ExportLoaderTask? updated = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(TaskStateId.Failed, updated!.State);
        Assert.Equal("SIT server unreachable", updated.StateReason);
        Assert.Equal(1, updated.FailureCount);
    }

    [Fact]
    public async Task DoWork_TaskNotYetDue_IsNeverClaimed()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(lastStart: DateTime.UtcNow, triggerEvery: TimeSpan.FromHours(1)), CancellationToken.None);
        bool wasCalled = false;
        exportRunner.Behaviour = (_, _) =>
        {
            wasCalled = true;
            return Task.FromResult(new SourceResourceLoadResult(0, 0, 0, 0, []));
        };

        await scheduler.DoWork(CancellationToken.None);

        Assert.False(wasCalled);
        ExportLoaderTask? unchanged = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.Ready, unchanged!.State);
    }

    [Fact]
    public async Task DoWork_StaleInProgressTask_IsReapedToFailed()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        // CoreApiWebApplicationFactory sets StaleInProgressAfter to 5 minutes for tests - 10 minutes
        // stale is comfortably past that without needing to wait in real time.
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(state: TaskStateId.InProgress, lastStart: DateTime.UtcNow.AddMinutes(-10)), CancellationToken.None);
        exportRunner.Behaviour = (_, _) => Task.FromResult(new SourceResourceLoadResult(0, 0, 0, 0, []));

        await scheduler.DoWork(CancellationToken.None);

        ExportLoaderTask? reaped = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(reaped);
        Assert.Equal(TaskStateId.Failed, reaped!.State);
        Assert.Equal("Reaped: exceeded expected run duration", reaped.StateReason);
        Assert.Equal(1, reaped.FailureCount);
    }

    [Fact]
    public async Task DoWork_FailedTaskWithinFailureAttemptCount_IsClaimedAndRun()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        // CoreApiWebApplicationFactory leaves FailureAttemptCount at its default of 3, so a Failed
        // task carrying FailureCount 3 is still Due.
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(state: TaskStateId.Failed, failureCount: 3), CancellationToken.None);
        bool wasCalled = false;
        exportRunner.Behaviour = (_, _) =>
        {
            wasCalled = true;
            return Task.FromResult(new SourceResourceLoadResult(0, 0, 0, 0, []));
        };

        await scheduler.DoWork(CancellationToken.None);

        Assert.True(wasCalled);
        ExportLoaderTask? updated = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.Completed, updated!.State);
        Assert.Equal(0, updated.FailureCount);
    }

    [Fact]
    public async Task DoWork_FailedTaskExceedingFailureAttemptCount_IsNeverClaimed()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(state: TaskStateId.Failed, failureCount: 4), CancellationToken.None);
        bool wasCalled = false;
        exportRunner.Behaviour = (_, _) =>
        {
            wasCalled = true;
            return Task.FromResult(new SourceResourceLoadResult(0, 0, 0, 0, []));
        };

        await scheduler.DoWork(CancellationToken.None);

        Assert.False(wasCalled);
        ExportLoaderTask? unchanged = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.Failed, unchanged!.State);
        Assert.Equal(4, unchanged.FailureCount);
    }
}
```

- [ ] **Step 8: Build and test**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: 0 errors.

Run: `dotnet test src/Abm.PD/Abm.PD.slnx`
Expected: all tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Application/TaskScheduler.cs
git add src/Abm.PD/Abm.PD.Core.Application/Settings/TaskSchedulerSettings.cs
git add src/Abm.PD/Abm.PD.Core.Application/DependencyInjection/ServiceCollectionExtension.cs
git add src/Abm.PD/Abm.PD.Core.Api/appsettings.json
git add src/Abm.PD/Abm.PD.Core.Api/appsettings.Development.json
git add src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/CoreApiWebApplicationFactory.cs
git add src/Abm.PD/Abm.PD.Core.Api.Tests/TestDoubles/ConfigurableExportRunner.cs
git add src/Abm.PD/Abm.PD.Core.Application.Tests/TaskSchedulerTests.cs
git add src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskSchedulerTests.cs
git rm src/Abm.PD/Abm.PD.Core.Application/ExportLoaderTaskScheduler.cs
git rm src/Abm.PD/Abm.PD.Core.Application/Settings/ExportLoaderTaskSchedulerSettings.cs
git rm src/Abm.PD/Abm.PD.Core.Application.Tests/ExportLoaderTaskSchedulerTests.cs
git rm src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskSchedulerTests.cs
git commit -m "$(cat <<'EOF'
Rename ExportLoaderTaskScheduler to TaskScheduler

Also renames ExportLoaderTaskSchedulerSettings to TaskSchedulerSettings (its
fields were already generic, just export-specifically named) and its config
section. TaskScheduler collides with System.Threading.Tasks.TaskScheduler in
exactly one file outside the Abm.PD.Core.Application namespace chain -
Abm.PD.Core.Api.Tests's scheduler test - fully qualified there.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UMWQENpvJ4gbbbbtmhodF8
EOF
)"
```

---

## Task 5: Rename `ExportLoaderTask` → `ExportTask` across Domain, Repository, Application, Api (production code)

Pure rename cascading from the entity outward: `ExportLoaderTask` → `ExportTask`,
`IExportLoaderTaskRepository`/`ExportLoaderTaskRepository` → `IExportTaskRepository`/`ExportTaskRepository`,
the EF configuration (including the physical owned-type table and FK constraint names),
`IExportRunner`/`ExportRunner`, `TaskScheduler`'s dispatch type, the API endpoint class/routes, and the
API contracts. This single task must land as one unit — none of these projects compile independently of
each other mid-rename, since each layer references the type the previous layer just renamed. Test
projects are **not** touched here (Task 7) and will not compile at the end of this task — that's
expected; verification here is `dotnet build` against the non-test projects only.

**Files:**
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Domain/Entities/ExportTask.cs` (was `ExportLoaderTask.cs`)
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportTaskRepository.cs` (was `IExportLoaderTaskRepository.cs`)
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportTaskRepository.cs` (was `ExportLoaderTaskRepository.cs`)
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Repository/Configuration/ExportTaskConfiguration.cs` (was `ExportLoaderTaskConfiguration.cs`)
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskBaseConfiguration.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application/IExportRunner.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application/ExportRunner.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application/TaskScheduler.cs`
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Api/Endpoints/ExportTaskEndpoints.cs` (was `ExportLoaderTaskEndpoints.cs`)
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskRequest.cs` (was `ExportLoaderTaskRequest.cs`)
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskResponse.cs` (was `ExportLoaderTaskResponse.cs`)
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskUpdateRequest.cs` (was `ExportLoaderTaskUpdateRequest.cs`)
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskParameterRequest.cs` (was `ExportLoaderTaskParameterRequest.cs`)
- Modify: `src/Abm.PD/Abm.PD.Core.Api/Program.cs`

**Interfaces:**
- Consumes: `TaskBase`, `TaskTypeId`, `TaskStateId`, `DataSource`, `ExportParameter` (existing, unchanged).
- Produces: `ExportTask : TaskBase` (was `ExportLoaderTask`). `IExportTaskRepository` (was
  `IExportLoaderTaskRepository`), same six CRUD members, `ExportTask` in place of `ExportLoaderTask`
  throughout. `ExportTaskRepository : IExportTaskRepository`. `IExportRunner.Run(ExportTask, ct)` (was
  `Run(ExportLoaderTask, ct)`). `ExportTaskEndpoints.MapExportTaskEndpoints()`, routes `/ExportTask`,
  `/ExportTask/{id:int}`. `ExportTaskRequest`, `ExportTaskResponse`, `ExportTaskUpdateRequest`,
  `ExportTaskParameterRequest`.

- [ ] **Step 1: Rename the entity**

Delete `src/Abm.PD/Abm.PD.Core.Domain/Entities/ExportLoaderTask.cs`. Create
`src/Abm.PD/Abm.PD.Core.Domain/Entities/ExportTask.cs`:

```csharp
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Entities;

public class ExportTask : TaskBase
{
    public ExportTask()
        : base(TaskTypeId.BulkImport)
    {
    }

    public required int DataSourceId { get; set; }

    public required DataSource DataSource { get; set; }

    public required ExportParameter Parameter { get; set; }
}
```

- [ ] **Step 2: Rename the repository interface**

Delete `src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportLoaderTaskRepository.cs`. Create
`src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportTaskRepository.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Repositories;

public interface IExportTaskRepository
{
    Task<IReadOnlyList<ExportTask>> GetAllAsync(CancellationToken cancellationToken);

    Task<ExportTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<ExportTask> AddAsync(
        ExportTask exportTask,
        CancellationToken cancellationToken);

    Task<ExportTask?> UpdateAsync(
        int id,
        ExportTask exportTask,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExportTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Rename the repository implementation**

Delete `src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportLoaderTaskRepository.cs`. Create
`src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportTaskRepository.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository.Repositories;

public class ExportTaskRepository(ProviderDirectoryDbContext dbContext) : IExportTaskRepository
{
    public async Task<IReadOnlyList<ExportTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ExportTasks
            .Include(x => x.DataSource)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<ExportTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.ExportTasks
            .Include(x => x.DataSource)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<ExportTask> AddAsync(
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        // The caller's DataSource instance usually comes from a no-tracking query (or a different
        // DbContext entirely), so it isn't in this context's change tracker yet. Attaching it first
        // lets EF's identity-resolution convention decide the right state - Added if its key is still
        // the CLR default (a genuinely new DataSource), Unchanged otherwise (an existing one) - rather
        // than Add's graph walk treating every untracked reachable entity as new and attempting to
        // re-insert an already-persisted DataSource, which violates its unique key.
        dbContext.Attach(exportTask.DataSource);
        dbContext.ExportTasks.Add(exportTask);
        await dbContext.SaveChangesAsync(cancellationToken);
        return exportTask;
    }

    public async Task<ExportTask?> UpdateAsync(
        int id,
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        ExportTask? existing = await dbContext.ExportTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        existing.Code = exportTask.Code;
        existing.DisplayName = exportTask.DisplayName;
        existing.Description = exportTask.Description;
        existing.State = exportTask.State;
        existing.StateReason = exportTask.StateReason;
        existing.TriggerEvery = exportTask.TriggerEvery;
        existing.ToStartAtUtc = exportTask.ToStartAtUtc;
        existing.ToEndAtUtc = exportTask.ToEndAtUtc;
        existing.LastStart = exportTask.LastStart;
        existing.LastEnd = exportTask.LastEnd;
        existing.DataSourceId = exportTask.DataSourceId;
        // CreatedUtc is deliberately never copied here - immutable after insert. UpdatedUtc always
        // is, caller-owned like every other field above.
        existing.UpdatedUtc = exportTask.UpdatedUtc;
        // Mutate the tracked owned instance in place rather than replacing the reference - EF Core's
        // change tracking for owned types is more reliable against property mutation than reassignment.
        existing.Parameter.Type = exportTask.Parameter.Type;
        existing.Parameter.Since = exportTask.Parameter.Since;
        existing.Parameter.TypeFilterList = exportTask.Parameter.TypeFilterList;

        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        ExportTask? existing = await dbContext.ExportTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        dbContext.ExportTasks.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ExportTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        IQueryable<ExportTask> query = dbContext.ExportTasks
            .Include(x => x.DataSource)
            .AsNoTracking();

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

- [ ] **Step 4: Rename the EF configuration, including the physical table and constraint names**

Delete `src/Abm.PD/Abm.PD.Core.Repository/Configuration/ExportLoaderTaskConfiguration.cs`. Create
`src/Abm.PD/Abm.PD.Core.Repository/Configuration/ExportTaskConfiguration.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class ExportTaskConfiguration : IEntityTypeConfiguration<ExportTask>
{
    public void Configure(EntityTypeBuilder<ExportTask> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // TPH mapping otherwise nullifies a derived-type column regardless of the CLR property's own
        // non-nullable int type - explicit Property(...).IsRequired() is needed on top of the
        // relationship's IsRequired() to actually get a NOT NULL data_source_id column.
        builder.Property(x => x.DataSourceId)
            .IsRequired();

        builder.HasOne(x => x.DataSource)
            .WithMany()
            .HasForeignKey(x => x.DataSourceId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsOne(x => x.Parameter, parameter =>
        {
            parameter.ToTable("export_task_parameter");

            // The auto-generated name for this FK truncates at Postgres's 63-character identifier
            // limit - name it explicitly instead.
            parameter.WithOwner().HasConstraintName("fk_export_task_parameter_task");

            parameter.Property(x => x.Type).HasColumnName("type");
            parameter.Property(x => x.Since).HasColumnName("since");
            parameter.Property(x => x.TypeFilterList).HasColumnName("type_filter_list");
        });
    }
}
```

- [ ] **Step 5: Update `TaskBaseConfiguration`'s discriminator**

In `src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskBaseConfiguration.cs`, replace:

```csharp
        builder.HasDiscriminator(x => x.TypeId)
            .HasValue<ExportLoaderTask>(TaskTypeId.BulkImport);
```

with:

```csharp
        builder.HasDiscriminator(x => x.TypeId)
            .HasValue<ExportTask>(TaskTypeId.BulkImport);
```

- [ ] **Step 6: Rename the `DbSet` on `ProviderDirectoryDbContext`**

In `src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs`, replace:

```csharp
    public DbSet<ExportLoaderTask> ExportLoaderTasks => Set<ExportLoaderTask>();
    public DbSet<TaskBase> Tasks => Set<TaskBase>();
```

with:

```csharp
    public DbSet<ExportTask> ExportTasks => Set<ExportTask>();
    public DbSet<TaskBase> Tasks => Set<TaskBase>();
```

- [ ] **Step 7: Update the Repository DI extension**

In `src/Abm.PD/Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs`, replace:

```csharp
        services.AddScoped<IExportLoaderTaskRepository, ExportLoaderTaskRepository>();
```

with:

```csharp
        services.AddScoped<IExportTaskRepository, ExportTaskRepository>();
```

- [ ] **Step 8: Rename `IExportRunner`'s parameter type**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Application/IExportRunner.cs` with:

```csharp
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application;

public interface IExportRunner
{
    Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 9: Rename `ExportRunner`'s parameter type**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Application/ExportRunner.cs` with:

```csharp
using Abm.PD.BulkExport;
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Models;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application;

public class ExportRunner(
    ILogger<ExportRunner> logger,
    IFhirExporter fhirExporter,
    ISourceResourceLoader sourceResourceLoader) : IExportRunner
{
    public async Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        Parameters parameters = FhirExportQuery.FromParameter(exportTask.Parameter);

        FhirBulkExportManifest? fhirBulkExportManifest =
            await fhirExporter.RequestDownloadManifest(parameters, cancellationToken);

        ArgumentNullException.ThrowIfNull(fhirBulkExportManifest);
        ArgumentNullException.ThrowIfNull(fhirExporter.JobId);

        logger.LogInformation(
            "JobId {JobId} ExportTask {TaskCode} download manifest received, persisting to source store",
            fhirExporter.JobId,
            exportTask.Code);

        IAsyncEnumerable<FhirBulkExportResource> streamedExportFileList = fhirExporter.StreamedExportFileList(cancellationToken);

        return await sourceResourceLoader.Load(
            exportResources: streamedExportFileList,
            jobId: fhirExporter.JobId,
            dataSource: exportTask.DataSource,
            cancellationToken: cancellationToken);
    }
}
```

- [ ] **Step 10: Update `TaskScheduler`'s dispatch type and re-fetch**

In `src/Abm.PD/Abm.PD.Core.Application/TaskScheduler.cs`, replace:

```csharp
public class TaskScheduler(
    ITaskRepository taskRepository,
    IExportLoaderTaskRepository exportLoaderTaskRepository,
    IServiceScopeFactory serviceScopeFactory,
```

with:

```csharp
public class TaskScheduler(
    ITaskRepository taskRepository,
    IExportTaskRepository exportTaskRepository,
    IServiceScopeFactory serviceScopeFactory,
```

Replace:

```csharp
            if (task is not ExportLoaderTask)
```

with:

```csharp
            if (task is not ExportTask)
```

Replace:

```csharp
                // task (from ITaskRepository) never has its DataSource navigation loaded - it's
                // re-fetched here through IExportLoaderTaskRepository, which Includes it, rather than
                // passed straight to IExportRunner.Run.
                ExportLoaderTask exportLoaderTask = await exportLoaderTaskRepository.GetByIdAsync(task.Id, cancellationToken)
                    ?? throw new InvalidOperationException($"ExportLoaderTask {task.Id} was claimed but no longer exists");

                SourceResourceLoadResult result = await exportRunner.Run(exportLoaderTask, cancellationToken);
```

with:

```csharp
                // task (from ITaskRepository) never has its DataSource navigation loaded - it's
                // re-fetched here through IExportTaskRepository, which Includes it, rather than
                // passed straight to IExportRunner.Run.
                ExportTask exportTask = await exportTaskRepository.GetByIdAsync(task.Id, cancellationToken)
                    ?? throw new InvalidOperationException($"ExportTask {task.Id} was claimed but no longer exists");

                SourceResourceLoadResult result = await exportRunner.Run(exportTask, cancellationToken);
```

Replace:

```csharp
                logger.LogError(exception, "ExportLoaderTask {TaskCode} failed", task.Code);
```

with:

```csharp
                logger.LogError(exception, "ExportTask {TaskCode} failed", task.Code);
```

- [ ] **Step 11: Rename the API endpoints class and routes**

Delete `src/Abm.PD/Abm.PD.Core.Api/Endpoints/ExportLoaderTaskEndpoints.cs`. Create
`src/Abm.PD/Abm.PD.Core.Api/Endpoints/ExportTaskEndpoints.cs`:

```csharp
using Abm.Core.Time;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Abm.PD.Core.Api.Endpoints;

public static class ExportTaskEndpoints
{
    public static IEndpointRouteBuilder MapExportTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ExportTask", GetAllOrSearch);
        endpoints.MapGet("/ExportTask/{id:int}", GetById);
        endpoints.MapPost("/ExportTask", Create);
        endpoints.MapPut("/ExportTask/{id:int}", Update);
        endpoints.MapDelete("/ExportTask/{id:int}", Delete);

        return endpoints;
    }

    private static async Task<IResult> GetAllOrSearch(
        string? code,
        TaskStateId? state,
        [FromQuery(Name = "last-start-from")] DateTime? lastStartFrom,
        [FromQuery(Name = "last-start-to")] DateTime? lastStartTo,
        IExportTaskRepository exportTaskRepository,
        IOptions<TimeSettings> timeSettings,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExportTask> exportTaskList = code is null && state is null && lastStartFrom is null && lastStartTo is null
            ? await exportTaskRepository.GetAllAsync(cancellationToken)
            : await exportTaskRepository.SearchAsync(
                code: code,
                state: state,
                lastStartFrom: lastStartFrom,
                lastStartTo: lastStartTo,
                cancellationToken: cancellationToken);

        return Results.Ok(exportTaskList.Select(
            x => ExportTaskResponse.FromEntity(x, timeSettings.Value.ServiceDefaultTimeZone)));
    }

    private static async Task<IResult> GetById(
        int id,
        IExportTaskRepository exportTaskRepository,
        IOptions<TimeSettings> timeSettings,
        CancellationToken cancellationToken)
    {
        ExportTask? exportTask = await exportTaskRepository.GetByIdAsync(id, cancellationToken);
        return exportTask is null
            ? Results.NotFound()
            : Results.Ok(ExportTaskResponse.FromEntity(exportTask, timeSettings.Value.ServiceDefaultTimeZone));
    }

    private static async Task<IResult> Create(
        ExportTaskRequest request,
        IExportTaskRepository exportTaskRepository,
        IDataSourceRepository dataSourceRepository,
        IOptions<TimeSettings> timeSettings,
        CancellationToken cancellationToken)
    {
        var dataSourceList = await dataSourceRepository.SearchAsync(code: request.DataSourceCode.Trim(), displayName: null, cancellationToken);
        if (dataSourceList.Count == 0)
        {
            return Results.BadRequest($"DataSourceCode {request.DataSourceCode} does not exist");
        }

        // Code carries a unique index at the database level - checking first turns what would
        // otherwise surface as an unhandled DbUpdateException on SaveChangesAsync into a clear 400.
        IReadOnlyList<ExportTask> existingWithCode = await exportTaskRepository.SearchAsync(
            code: request.Code,
            state: null,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: cancellationToken);
        if (existingWithCode.Count > 0)
        {
            return Results.BadRequest($"ExportTask with Code '{request.Code}' already exists");
        }

        DateTime nowUtc = DateTime.UtcNow;
        ExportTask exportTask = new()
        {
            Code = request.Code,
            DisplayName = request.DisplayName,
            Description = request.Description,
            State = request.State,
            StateReason = request.StateReason,
            TriggerEvery = request.TriggerEvery,
            // DateTimeOffset.UtcDateTime always yields Kind=Utc regardless of the offset the caller
            // sent - Npgsql rejects a DateTime with Kind=Local or Unspecified for a "timestamp with
            // time zone" column, which a plain DateTime? here could otherwise carry depending on how
            // the incoming JSON's offset was parsed.
            ToStartAtUtc = request.ToStartAtUtc?.UtcDateTime,
            ToEndAtUtc = request.ToEndAtUtc?.UtcDateTime,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = null,
            LastEnd = null,
            DataSourceId = dataSourceList.First().Id,
            DataSource = dataSourceList.First(),
            Parameter = new ExportParameter
            {
                Type = request.Parameter.Type,
                // Npgsql only accepts DateTimeOffset.Offset == 0 for a "timestamp with time zone"
                // column - the caller may have sent any offset, so normalise to UTC before storing.
                Since = request.Parameter.Since?.ToUniversalTime(),
                TypeFilterList = request.Parameter.TypeFilterList,
            },
        };
        exportTask = await exportTaskRepository.AddAsync(exportTask, cancellationToken);
        return Results.Created(
            $"/ExportTask/{exportTask.Id}",
            ExportTaskResponse.FromEntity(exportTask, timeSettings.Value.ServiceDefaultTimeZone));
    }

    private static async Task<IResult> Update(
        int id,
        ExportTaskUpdateRequest request,
        IExportTaskRepository exportTaskRepository,
        IOptions<TimeSettings> timeSettings,
        CancellationToken cancellationToken)
    {
        // Code is deliberately absent from ExportTaskUpdateRequest - it is immutable after
        // creation. DataSourceCode is present but is also immutable after creation, so it is
        // validated below against the existing row rather than copied across.
        ExportTask? existing = await exportTaskRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }
        
        if (existing.State == TaskStateId.InProgress)
        {
            return Results.BadRequest($"The ExportTask State={existing.State}', " +
                                      $"can not modify a task while {nameof(TaskStateId.InProgress)} ");
        }
        
        if (!existing.DataSource.Code.Equals(request.DataSourceCode.Trim()))
        {
            return Results.BadRequest($"The ExportTask's DataSourceCode: {request.DataSourceCode.Trim()}', " +
                                      $"can not be updated. Current DataSourceCode is {existing.DataSource.Code} ");
        }

        existing.DisplayName = request.DisplayName;
        existing.Description = request.Description;
        existing.State = request.State;
        existing.StateReason = request.StateReason;
        existing.TriggerEvery = request.TriggerEvery;
        existing.ToStartAtUtc = request.ToStartAtUtc?.UtcDateTime;
        existing.ToEndAtUtc = request.ToEndAtUtc?.UtcDateTime;
        existing.UpdatedUtc = DateTime.UtcNow;
        existing.Parameter.Type = request.Parameter.Type;
        // Npgsql only accepts DateTimeOffset.Offset == 0 for a "timestamp with time zone" column -
        // a round-tripped GET response carries ServiceDefaultTimeZone's offset (e.g. +10:00), so
        // normalise back to UTC before storing.
        existing.Parameter.Since = request.Parameter.Since?.ToUniversalTime();
        existing.Parameter.TypeFilterList = request.Parameter.TypeFilterList;

        ExportTask? updated = await exportTaskRepository.UpdateAsync(id, existing, cancellationToken);
        if (updated is null)
        {
            return Results.NotFound();
        }

        // UpdateAsync's internal re-fetch doesn't Include the DataSource navigation, and DataSourceId
        // never changes via update, so carry it over from the already-loaded `existing` rather than
        // returning a response with a null DataSource.
        updated.DataSource = existing.DataSource;
        return Results.Ok(ExportTaskResponse.FromEntity(updated, timeSettings.Value.ServiceDefaultTimeZone));
    }

    private static async Task<IResult> Delete(
        int id,
        IExportTaskRepository exportTaskRepository,
        CancellationToken cancellationToken)
    {
        bool deleted = await exportTaskRepository.DeleteAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
```

- [ ] **Step 12: Rename the API contracts**

Delete `src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportLoaderTaskParameterRequest.cs`. Create
`src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskParameterRequest.cs`:

```csharp
namespace Abm.PD.Core.Api.Contracts;

public record ExportTaskParameterRequest(string Type, DateTimeOffset? Since, List<string> TypeFilterList);
```

Delete `src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportLoaderTaskRequest.cs`. Create
`src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskRequest.cs`:

```csharp
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

public record ExportTaskRequest(
    string Code,
    string DisplayName,
    string? Description,
    TaskStateId State,
    string? StateReason,
    TimeSpan TriggerEvery,
    DateTimeOffset? ToStartAtUtc,
    DateTimeOffset? ToEndAtUtc,
    string DataSourceCode,
    ExportTaskParameterRequest Parameter);
```

Delete `src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportLoaderTaskUpdateRequest.cs`. Create
`src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskUpdateRequest.cs`:

```csharp
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

/// <summary>
/// PUT body shape for updating an ExportTask. Deliberately excludes the server-controlled
/// fields that <see cref="ExportTaskResponse"/> returns (Id, TypeId, Code, CreatedUtc,
/// UpdatedUtc, LastStartUtc, LastEndUtc) so the exact JSON body returned by a prior GET can be PUT
/// straight back without editing it first - System.Text.Json ignores the extra properties rather
/// than erroring on them. DataSourceCode is carried over rather than excluded - it round-trips
/// unchanged, but the handler rejects a PUT that tries to change it since it is immutable after
/// creation.
/// </summary>
public record ExportTaskUpdateRequest(
    string DisplayName,
    string? Description,
    TaskStateId State,
    string? StateReason,
    string DataSourceCode,
    TimeSpan TriggerEvery,
    DateTimeOffset? ToStartAtUtc,
    DateTimeOffset? ToEndAtUtc,
    ExportTaskParameterRequest Parameter);
```

Delete `src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportLoaderTaskResponse.cs`. Create
`src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskResponse.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

/// <summary>
/// The shape returned by GET/GET-search (and echoed back by POST/PUT) for an ExportTask.
/// Every timestamp is converted from its UTC storage into the configured ServiceDefaultTimeZone and
/// surfaced as a DateTimeOffset, so API consumers see it as wall-clock local time without losing the
/// absolute instant. This response can be PUT straight back to <c>/ExportTask/{id}</c> -
/// <see cref="ExportTaskUpdateRequest"/> deliberately omits the server-controlled fields here
/// (Id, TypeId, Code, CreatedUtc, UpdatedUtc, LastStartUtc, LastEndUtc) so they are silently ignored
/// rather than erroring the update. DataSourceCode is present on both, but immutable after creation -
/// the update handler rejects a PUT that tries to change it.
/// </summary>
public record ExportTaskResponse(
    int Id,
    TaskTypeId TypeId,
    string Code,
    string DisplayName,
    string? Description,
    TaskStateId State,
    string? StateReason,
    TimeSpan TriggerEvery,
    DateTimeOffset? ToStartAtUtc,
    DateTimeOffset? ToEndAtUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? LastStartUtc,
    DateTimeOffset? LastEndUtc,
    string DataSourceCode,
    ExportTaskParameterRequest Parameter)
{
    public static ExportTaskResponse FromEntity(ExportTask exportTask, TimeSpan serviceDefaultTimeZone)
    {
        return new ExportTaskResponse(
            Id: exportTask.Id,
            TypeId: exportTask.TypeId,
            Code: exportTask.Code,
            DisplayName: exportTask.DisplayName,
            Description: exportTask.Description,
            State: exportTask.State,
            StateReason: exportTask.StateReason,
            TriggerEvery: exportTask.TriggerEvery,
            ToStartAtUtc: ToServiceOffset(exportTask.ToStartAtUtc, serviceDefaultTimeZone),
            ToEndAtUtc: ToServiceOffset(exportTask.ToEndAtUtc, serviceDefaultTimeZone),
            CreatedUtc: ToServiceOffset(exportTask.CreatedUtc, serviceDefaultTimeZone),
            UpdatedUtc: ToServiceOffset(exportTask.UpdatedUtc, serviceDefaultTimeZone),
            LastStartUtc: ToServiceOffset(exportTask.LastStart, serviceDefaultTimeZone),
            LastEndUtc: ToServiceOffset(exportTask.LastEnd, serviceDefaultTimeZone),
            DataSourceCode: exportTask.DataSource.Code,
            Parameter: new ExportTaskParameterRequest(
                Type: exportTask.Parameter.Type,
                Since: exportTask.Parameter.Since?.ToOffset(serviceDefaultTimeZone),
                TypeFilterList: exportTask.Parameter.TypeFilterList));
    }

    private static DateTimeOffset ToServiceOffset(DateTime utcDateTime, TimeSpan serviceDefaultTimeZone) =>
        new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc)).ToOffset(serviceDefaultTimeZone);

    private static DateTimeOffset? ToServiceOffset(DateTime? utcDateTime, TimeSpan serviceDefaultTimeZone) =>
        utcDateTime is null ? null : ToServiceOffset(utcDateTime.Value, serviceDefaultTimeZone);
}
```

- [ ] **Step 13: Update `Program.cs`**

In `src/Abm.PD/Abm.PD.Core.Api/Program.cs`, replace:

```csharp
app.MapExportLoaderTaskEndpoints();
```

with:

```csharp
app.MapExportTaskEndpoints();
```

- [ ] **Step 14: Build the non-test projects**

Run: `dotnet build src/Abm.PD/Abm.PD.Core.Api/Abm.PD.Core.Api.csproj`
Expected: 0 errors (this transitively builds `Abm.PD.Core.Domain`, `Abm.PD.Core.Repository`,
`Abm.PD.Core.Application`, `Abm.PD.BulkExport`, `Abm.Core` too).

Run: `dotnet build src/Abm.PD/Abm.PD.Console/Abm.PD.Console.csproj`
Expected: 0 errors.

Do **not** run `dotnet build src/Abm.PD/Abm.PD.slnx` or `dotnet test` yet — the test projects still
reference `ExportLoaderTask`/`ExportLoaderTaskRequest`/etc. and will not compile until Task 7. That's
expected at this point in the plan.

- [ ] **Step 15: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Domain/Entities/ExportTask.cs
git add src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportTaskRepository.cs
git add src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportTaskRepository.cs
git add src/Abm.PD/Abm.PD.Core.Repository/Configuration/ExportTaskConfiguration.cs
git add src/Abm.PD/Abm.PD.Core.Repository/Configuration/TaskBaseConfiguration.cs
git add src/Abm.PD/Abm.PD.Core.Repository/ProviderDirectoryDbContext.cs
git add src/Abm.PD/Abm.PD.Core.Repository/DependencyInjection/ServiceCollectionExtension.cs
git add src/Abm.PD/Abm.PD.Core.Application/IExportRunner.cs
git add src/Abm.PD/Abm.PD.Core.Application/ExportRunner.cs
git add src/Abm.PD/Abm.PD.Core.Application/TaskScheduler.cs
git add src/Abm.PD/Abm.PD.Core.Api/Endpoints/ExportTaskEndpoints.cs
git add src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskRequest.cs
git add src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskResponse.cs
git add src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskUpdateRequest.cs
git add src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportTaskParameterRequest.cs
git add src/Abm.PD/Abm.PD.Core.Api/Program.cs
git rm src/Abm.PD/Abm.PD.Core.Domain/Entities/ExportLoaderTask.cs
git rm src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportLoaderTaskRepository.cs
git rm src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportLoaderTaskRepository.cs
git rm src/Abm.PD/Abm.PD.Core.Repository/Configuration/ExportLoaderTaskConfiguration.cs
git rm src/Abm.PD/Abm.PD.Core.Api/Endpoints/ExportLoaderTaskEndpoints.cs
git rm src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportLoaderTaskRequest.cs
git rm src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportLoaderTaskResponse.cs
git rm src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportLoaderTaskUpdateRequest.cs
git rm src/Abm.PD/Abm.PD.Core.Api/Contracts/ExportLoaderTaskParameterRequest.cs
git commit -m "$(cat <<'EOF'
Rename ExportLoaderTask entity to ExportTask across production code

Entity, repository, EF configuration (including the physical owned-type
table export_loader_task_parameter -> export_task_parameter and its FK
constraint), IExportRunner/ExportRunner, TaskScheduler's dispatch type, API
endpoint class/routes (/ExportLoaderTask -> /ExportTask), and contracts.
Test projects are updated separately (next commit lands the migration
regeneration this schema rename requires; the one after fixes the tests).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UMWQENpvJ4gbbbbtmhodF8
EOF
)"
```

---

## Task 6: Delete the existing migrations and regenerate a single fresh `InitialCreate`

Still in startup/dev mode (per the spec), so the schema rename in Task 5 is carried through by
replacing all three existing migrations with one fresh one, rather than adding a rename migration on
top of the old names. `dotnet ef migrations add` only needs the model (via
`DesignTimeDbContextFactory`) — it does not open a database connection, so this is safe to run without
Docker or a live Postgres instance.

**Files:**
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260913120952_InitialCreate.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260913120952_InitialCreate.Designer.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260913135635_AddSourceResource.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260913135635_AddSourceResource.Designer.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260914083335_AddTaskFailureCount.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260914083335_AddTaskFailureCount.Designer.cs`
- Delete: `src/Abm.PD/Abm.PD.Core.Repository/Migrations/ProviderDirectoryDbContextModelSnapshot.cs`
- Create: a new `<timestamp>_InitialCreate.cs` + `.Designer.cs` + regenerated
  `ProviderDirectoryDbContextModelSnapshot.cs` (generated by the `dotnet ef` command below, not
  hand-written)

**Interfaces:**
- Consumes: the fully-renamed model from Task 5 (`ExportTask`, `export_task_parameter`,
  `fk_export_task_parameter_task`, `Tasks`/`ExportTasks` DbSets on `ProviderDirectoryDbContext`).
- Produces: one migration, `InitialCreate`, whose `Up()` creates every table/index/constraint the
  current model needs, under the renamed names.

- [ ] **Step 1: Confirm the `dotnet-ef` tool is available**

Run: `dotnet ef --version`
Expected: prints a version. If instead you get "No executable found matching command 'dotnet-ef'",
install it first: `dotnet tool install --global dotnet-ef`, then retry `dotnet ef --version`.

- [ ] **Step 2: Delete the three existing migrations and the model snapshot**

```bash
git rm src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260913120952_InitialCreate.cs
git rm src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260913120952_InitialCreate.Designer.cs
git rm src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260913135635_AddSourceResource.cs
git rm src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260913135635_AddSourceResource.Designer.cs
git rm src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260914083335_AddTaskFailureCount.cs
git rm src/Abm.PD/Abm.PD.Core.Repository/Migrations/20260914083335_AddTaskFailureCount.Designer.cs
git rm src/Abm.PD/Abm.PD.Core.Repository/Migrations/ProviderDirectoryDbContextModelSnapshot.cs
```

- [ ] **Step 3: Regenerate a single fresh `InitialCreate` migration**

Run, with working directory `src/Abm.PD`:

```bash
cd src/Abm.PD
dotnet ef migrations add InitialCreate --project Abm.PD.Core.Repository --startup-project Abm.PD.Core.Repository
```

Expected: `Build started...`, `Build succeeded.`, `Done.`, and three new files under
`Abm.PD.Core.Repository/Migrations/`: `<timestamp>_InitialCreate.cs`,
`<timestamp>_InitialCreate.Designer.cs`, and a regenerated `ProviderDirectoryDbContextModelSnapshot.cs`.

- [ ] **Step 4: Inspect the generated migration for the renamed objects**

Open the new `<timestamp>_InitialCreate.cs` and confirm its `Up()` method:
- Creates a table named `export_task_parameter` (not `export_loader_task_parameter`), with primary key
  `pk_export_task_parameter` and foreign key `fk_export_task_parameter_task` referencing `task(id)`.
- Creates the `task` table with a nullable `data_source_id` column (unchanged from before — TPH still
  nullifies a derived-type column at the base-table level; `ExportTaskConfiguration`'s own
  `Property(x => x.DataSourceId).IsRequired()` layers a NOT NULL on top of this in the model, but the
  generated column DDL for a TPH base table is still nullable here, matching the pre-rename migration's
  same `data_source_id` column).
- `InsertData` seeds `task_state` (5 rows) and `task_type` (1 row, `BulkImport`) exactly as the deleted
  migration did.

If anything looks wrong (wrong table name, missing FK, missing seed data), do not proceed — re-check
that Task 5's Step 4 (`ExportTaskConfiguration`) and Step 6 (`ProviderDirectoryDbContext`) actually
landed as written before regenerating.

- [ ] **Step 5: Build**

Run: `dotnet build src/Abm.PD/Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj`
Expected: 0 errors — the generated migration code itself must compile.

- [ ] **Step 6: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Repository/Migrations/
git commit -m "$(cat <<'EOF'
Regenerate InitialCreate migration for the ExportTask rename

Deletes the three existing migrations (all from the last two days, still
startup/dev mode) and replaces them with one fresh InitialCreate reflecting
the renamed schema - export_task_parameter table and
fk_export_task_parameter_task constraint in place of the
export_loader_task_-prefixed names. Does not touch any live database;
dropping and recreating a local dev database against this migration is a
manual follow-up outside this change.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UMWQENpvJ4gbbbbtmhodF8
EOF
)"
```

---

## Task 7: Rename `ExportLoaderTask` → `ExportTask` across every test file; full solution verification

The last task — every remaining `ExportLoaderTask`/`IExportLoaderTaskRepository`/
`ExportLoaderTaskRequest`/etc. reference in `Abm.PD.Core.Application.Tests` and
`Abm.PD.Core.Api.Tests` is renamed to match Tasks 5-6, plus the file renames/splits the spec calls for.
This is the first point since Task 4 where the full solution (including every test project) compiles
and the full suite — now running against the fresh `InitialCreate` migration from Task 6 — can be run
end to end.

**Files:**
- Modify: `src/Abm.PD/Abm.PD.Core.Application.Tests/ExportRunnerTests.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application.Tests/TaskSchedulerTests.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryTaskRepository.cs`
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryExportTaskRepository.cs`
  (was `InMemoryExportLoaderTaskRepository.cs`)
- Modify: `src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/ThrowingExportRunner.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/ScopeTrackingExportRunner.cs`
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportTaskCrudTests.cs`
  (was `ExportLoaderTaskCrudTests.cs`)
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportTaskMappingTests.cs`
  (was `ExportLoaderTaskMappingTests.cs`)
- Delete + recreate as: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportTaskRepositoryTests.cs`
  (was `ExportLoaderTaskRepositoryTests.cs`)
- Modify: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskRepositoryTests.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskSchedulerTests.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Api.Tests/TestDoubles/ConfigurableExportRunner.cs`

**Interfaces:**
- Consumes: everything Task 5 renamed (`ExportTask`, `IExportTaskRepository`, `ExportTaskRequest`,
  `ExportTaskResponse`, `ExportTaskUpdateRequest`, `ExportTaskParameterRequest`, `/ExportTask` routes)
  and Task 6's regenerated schema.
- Produces: nothing new — every test double/fixture keeps its existing shape, just retyped.

- [ ] **Step 1: Update `ExportRunnerTests.cs`**

In `src/Abm.PD/Abm.PD.Core.Application.Tests/ExportRunnerTests.cs`, replace the full file with:

```csharp
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.Tests.TestDoubles;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Tests;

public class ExportRunnerTests
{
    private static ExportTask NewTask()
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new ExportTask
        {
            Code = "test-task",
            DisplayName = "Test task",
            Description = null,
            State = TaskStateId.InProgress,
            StateReason = null,
            TriggerEvery = TimeSpan.FromHours(24),
            ToStartAtUtc = null,
            ToEndAtUtc = null,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = nowUtc,
            LastEnd = null,
            DataSourceId = 1,
            DataSource = new DataSource { Id = 1, Code = "test-data-source", DisplayName = "Test Data Source" },
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    [Fact]
    public async Task Run_StreamsExportIntoSourceResourceLoader_ReturnsLoadResult()
    {
        FakeFhirExporter fakeExporter = new();
        fakeExporter.ResourcesToStream.Add(new FhirBulkExportResource(
            Resource: new Patient { Id = "1" },
            ManifestOutputType: "Patient",
            SourceUrl: new Uri("https://export.test/Patient.ndjson"),
            LineNumber: 1));
        FakeSourceResourceLoader fakeLoader = new()
        {
            ResultToReturn = new SourceResourceLoadResult(
                SubmittedCount: 1, CommittedCount: 1, FailedCount: 0, BatchCount: 1, RetainedFailures: []),
        };
        ExportRunner runner = new(NullLogger<ExportRunner>.Instance, fakeExporter, fakeLoader);
        ExportTask task = NewTask();

        SourceResourceLoadResult result = await runner.Run(task, CancellationToken.None);

        Assert.Equal(1, result.CommittedCount);
        Assert.Single(fakeLoader.ReceivedResources);
        Assert.Equal(fakeExporter.JobId, fakeLoader.ReceivedJobId);
        Assert.Same(task.DataSource, fakeLoader.ReceivedDataSource);
        Assert.NotNull(fakeExporter.ReceivedParameters);
    }

    [Fact]
    public async Task Run_NullManifest_ThrowsArgumentNullException()
    {
        FakeFhirExporter fakeExporter = new() { ManifestToReturn = null };
        FakeSourceResourceLoader fakeLoader = new();
        ExportRunner runner = new(NullLogger<ExportRunner>.Instance, fakeExporter, fakeLoader);

        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.Run(NewTask(), CancellationToken.None));
    }
}
```

- [ ] **Step 2: Update `TestDoubles/ThrowingExportRunner.cs`**

Replace the full contents of
`src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/ThrowingExportRunner.cs` with:

```csharp
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// Always throws, so TaskScheduler.DoWork's catch block runs - used to assert FailureCount
// behaviour without a real FhirBulkExporter failure.
public sealed class ThrowingExportRunner : IExportRunner
{
    public Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated export failure");
    }
}
```

- [ ] **Step 3: Update `TestDoubles/ScopeTrackingExportRunner.cs`**

Replace the full contents of
`src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/ScopeTrackingExportRunner.cs` with:

```csharp
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// Registered Scoped in the test's ServiceCollection so a fresh InstanceId is minted once per
// CreateScope() call - exactly the behaviour Finding 1's regression test is asserting on.
public sealed class ScopeTrackingExportRunner(List<(int TaskId, Guid InstanceId)> calls) : IExportRunner
{
    private readonly Guid InstanceId = Guid.NewGuid();

    public Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        calls.Add((exportTask.Id, InstanceId));
        return Task.FromResult(new SourceResourceLoadResult(
            SubmittedCount: 0, CommittedCount: 0, FailedCount: 0, BatchCount: 0, RetainedFailures: []));
    }
}
```

- [ ] **Step 4: Rename `InMemoryExportLoaderTaskRepository` to `InMemoryExportTaskRepository`**

Delete `src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryExportLoaderTaskRepository.cs`.
Create `src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryExportTaskRepository.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// TaskScheduler re-fetches the fully-loaded ExportTask by Id after claiming it (see the design
// spec's "why the claimed instance can't be used directly" callout) - GetByIdAsync must actually
// work for that flow to be exercised in these tests, unlike the other CRUD members, which nothing
// here calls.
public sealed class InMemoryExportTaskRepository(List<ExportTask> tasks) : IExportTaskRepository
{
    public Task<IReadOnlyList<ExportTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ExportTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(tasks.SingleOrDefault(t => t.Id == id));
    }

    public Task<ExportTask> AddAsync(
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ExportTask?> UpdateAsync(
        int id,
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<ExportTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
```

`InMemoryTaskRepository.cs` (Task 3) needs no changes — it's already typed purely in terms of
`TaskBase`/`ITaskRepository`, neither of which this rename touches.

- [ ] **Step 5: Update `TaskSchedulerTests.cs` (Application.Tests)**

Replace the full contents of `src/Abm.PD/Abm.PD.Core.Application.Tests/TaskSchedulerTests.cs` with:

```csharp
using Abm.Core.Time;
using Abm.PD.Core.Application.Settings;
using Abm.PD.Core.Application.Tests.TestDoubles;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Tests;

public class TaskSchedulerTests
{
    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset Now => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static ExportTask NewTask(
        int id,
        string code,
        TaskStateId state = TaskStateId.Ready,
        int failureCount = 0)
    {
        return new ExportTask
        {
            Id = id,
            Code = code,
            DisplayName = $"Task {code}",
            Description = null,
            State = state,
            StateReason = null,
            TriggerEvery = TimeSpan.FromHours(24),
            ToStartAtUtc = null,
            ToEndAtUtc = null,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            LastStart = null,
            LastEnd = null,
            FailureCount = failureCount,
            DataSourceId = 1,
            DataSource = new DataSource { Id = 1, Code = "test-data-source", DisplayName = "Test Data Source" },
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    private static ServiceProvider BuildProvider(
        List<ExportTask> seededTasks,
        IExportRunner exportRunner,
        int failureAttemptCount = 3)
    {
        ServiceCollection services = new();
        services.AddSingleton<IExportRunner>(exportRunner);
        services.AddSingleton<ITaskRepository>(new InMemoryTaskRepository(seededTasks.Cast<TaskBase>().ToList()));
        services.AddSingleton<IExportTaskRepository>(new InMemoryExportTaskRepository(seededTasks));
        services.AddSingleton<IDateTimeProvider>(new FixedDateTimeProvider());
        services.AddSingleton<IOptions<TaskSchedulerSettings>>(
            Options.Create(new TaskSchedulerSettings { FailureAttemptCount = failureAttemptCount }));
        services.AddSingleton<ILogger<TaskScheduler>>(NullLogger<TaskScheduler>.Instance);
        services.AddScoped<TaskScheduler>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DoWork_TwoDueTasks_EachGetsItsOwnExportRunnerInstance()
    {
        List<(int TaskId, Guid InstanceId)> calls = [];
        List<ExportTask> seededTasks = [NewTask(1, "task-one"), NewTask(2, "task-two")];

        ServiceCollection services = new();
        services.AddSingleton(calls);
        services.AddScoped<IExportRunner, ScopeTrackingExportRunner>();
        services.AddSingleton<ITaskRepository>(new InMemoryTaskRepository(seededTasks.Cast<TaskBase>().ToList()));
        services.AddSingleton<IExportTaskRepository>(new InMemoryExportTaskRepository(seededTasks));
        services.AddSingleton<IDateTimeProvider>(new FixedDateTimeProvider());
        services.AddSingleton<IOptions<TaskSchedulerSettings>>(
            Options.Create(new TaskSchedulerSettings()));
        services.AddSingleton<ILogger<TaskScheduler>>(NullLogger<TaskScheduler>.Instance);
        services.AddScoped<TaskScheduler>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        // Simulates the tick engine's own per-tick outer scope - the scheduler itself is resolved
        // once per tick, exactly as ITimedHostedService driving it would do.
        using IServiceScope tickScope = provider.CreateScope();
        TaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<TaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Equal(2, calls.Count);
        Assert.NotEqual(calls[0].InstanceId, calls[1].InstanceId);
    }

    [Fact]
    public async Task DoWork_RunnerThrows_IncrementsFailureCountAndSetsFailed()
    {
        List<ExportTask> seededTasks = [NewTask(1, "task-one")];
        await using ServiceProvider provider = BuildProvider(seededTasks, new ThrowingExportRunner());
        using IServiceScope tickScope = provider.CreateScope();
        TaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<TaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Equal(TaskStateId.Failed, seededTasks[0].State);
        Assert.Equal(1, seededTasks[0].FailureCount);
    }

    [Fact]
    public async Task DoWork_RunnerSucceeds_ResetsFailureCountToZero()
    {
        List<ExportTask> seededTasks = [NewTask(1, "task-one", failureCount: 2)];
        await using ServiceProvider provider = BuildProvider(seededTasks, new ScopeTrackingExportRunner([]));
        using IServiceScope tickScope = provider.CreateScope();
        TaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<TaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Equal(TaskStateId.Completed, seededTasks[0].State);
        Assert.Equal(0, seededTasks[0].FailureCount);
    }

    [Fact]
    public async Task DoWork_FailedTaskWithinFailureAttemptCount_IsRun()
    {
        List<(int TaskId, Guid InstanceId)> calls = [];
        List<ExportTask> seededTasks =
            [NewTask(1, "task-one", state: TaskStateId.Failed, failureCount: 3)];
        await using ServiceProvider provider = BuildProvider(
            seededTasks, new ScopeTrackingExportRunner(calls), failureAttemptCount: 3);
        using IServiceScope tickScope = provider.CreateScope();
        TaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<TaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Single(calls);
    }

    [Fact]
    public async Task DoWork_FailedTaskExceedingFailureAttemptCount_IsNotRun()
    {
        List<(int TaskId, Guid InstanceId)> calls = [];
        List<ExportTask> seededTasks =
            [NewTask(1, "task-one", state: TaskStateId.Failed, failureCount: 4)];
        await using ServiceProvider provider = BuildProvider(
            seededTasks, new ScopeTrackingExportRunner(calls), failureAttemptCount: 3);
        using IServiceScope tickScope = provider.CreateScope();
        TaskScheduler scheduler = tickScope.ServiceProvider.GetRequiredService<TaskScheduler>();

        await scheduler.DoWork(CancellationToken.None);

        Assert.Empty(calls);
    }
}
```

- [ ] **Step 6: Update `ConfigurableExportRunner.cs` (Api.Tests)**

Replace the full contents of
`src/Abm.PD/Abm.PD.Core.Api.Tests/TestDoubles/ConfigurableExportRunner.cs` with:

```csharp
using Abm.PD.Core.Application;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Api.Tests.TestDoubles;

/// <summary>
/// A per-test-configurable IExportRunner substituted for the real one in CoreApiWebApplicationFactory,
/// so TaskScheduler never makes a real FHIR HTTP call during any Abm.PD.Core.Api.Tests run.
/// Registered as a singleton - tests within the shared IntegrationTestCollection run sequentially, so
/// setting Behaviour per test is safe.
/// </summary>
public sealed class ConfigurableExportRunner : IExportRunner
{
    public Func<ExportTask, CancellationToken, Task<SourceResourceLoadResult>>? Behaviour { get; set; }

    public Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        if (Behaviour is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ConfigurableExportRunner)}.{nameof(Behaviour)} was not set before the scheduler ran.");
        }

        return Behaviour(exportTask, cancellationToken);
    }
}
```

- [ ] **Step 7: Rename `ExportLoaderTaskCrudTests.cs` → `ExportTaskCrudTests.cs`**

Delete `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskCrudTests.cs`. Create
`src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportTaskCrudTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportTaskCrudTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    // The API serialises TaskStateId/TaskTypeId as their member name (see Program.cs's
    // ConfigureHttpJsonOptions), so responses containing those enums need the matching converter to
    // deserialise here - System.Net.Http.Json otherwise uses JsonSerializerOptions.Default, which only
    // understands the underlying int.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<string> CreateDataSourceCodeAsync()
    {
        string code = Guid.NewGuid().ToString();
        DataSourceRequest request = new(code, "Provider Connect Australia");
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/DataSource", request);
        response.EnsureSuccessStatusCode();
        return code;
    }

    private static ExportTaskRequest NewRequest(
        string code,
        string dataSourceCode,
        TaskStateId state = TaskStateId.Ready,
        DateTimeOffset? toStartAtUtc = null,
        DateTimeOffset? since = null)
    {
        return new ExportTaskRequest(
            Code: code,
            DisplayName: $"Task {code}",
            Description: "Nightly bulk import",
            State: state,
            StateReason: null,
            TriggerEvery: TimeSpan.FromHours(24),
            ToStartAtUtc: toStartAtUtc,
            ToEndAtUtc: null,
            DataSourceCode: dataSourceCode,
            Parameter: new ExportTaskParameterRequest(
                Type: "Patient",
                Since: since,
                TypeFilterList: ["Patient"]));
    }

    private static ExportTaskUpdateRequest AsUpdateRequest(ExportTaskResponse response) => new(
        DisplayName: response.DisplayName,
        Description: response.Description,
        State: response.State,
        StateReason: response.StateReason,
        DataSourceCode: response.DataSourceCode,
        TriggerEvery: response.TriggerEvery,
        ToStartAtUtc: response.ToStartAtUtc,
        ToEndAtUtc: response.ToEndAtUtc,
        Parameter: response.Parameter);

    [Fact]
    public async Task Create_ValidRequest_Returns201WithCreatedExportTask()
    {
        string dataSourceCode = await CreateDataSourceCodeAsync();
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), dataSourceCode);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportTask", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ExportTaskResponse? created = await response.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(request.Code, created!.Code);
        Assert.Equal(request.DisplayName, created.DisplayName);
        Assert.Equal(TaskTypeId.BulkImport, created.TypeId);
        Assert.Equal(new[] { "Patient" }, created.Parameter.TypeFilterList);
        Assert.Equal(dataSourceCode, created.DataSourceCode);
        Assert.Null(created.LastStartUtc);
        Assert.Null(created.LastEndUtc);
        Assert.NotEqual(default, created.CreatedUtc);
        Assert.NotEqual(default, created.UpdatedUtc);
    }

    [Fact]
    public async Task Create_ValidRequest_SerialisesTypeIdAndStateAsEnumNamesNotIntegers()
    {
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceCodeAsync());

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportTask", request);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"typeId\":\"BulkImport\"", body);
        Assert.Contains("\"state\":\"Ready\"", body);
        Assert.DoesNotContain("\"typeId\":1", body);
        Assert.DoesNotContain("\"state\":1", body);
    }

    [Fact]
    public async Task Create_WithNonUtcOffsetToStartAtUtc_Succeeds()
    {
        // ExportTaskRequest.ToStartAtUtc is a DateTimeOffset so it carries its offset
        // explicitly - a plain DateTime? here could otherwise deserialise with Kind=Local for a
        // non-zero offset, which Npgsql rejects for a "timestamp with time zone" column.
        string dataSourceCode = await CreateDataSourceCodeAsync();
        DateTimeOffset toStartAtUtc = new(2026, 9, 15, 8, 0, 0, TimeSpan.FromHours(10));
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), dataSourceCode, toStartAtUtc: toStartAtUtc);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportTask", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ExportTaskResponse? created = await response.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(toStartAtUtc, created!.ToStartAtUtc);
    }

    [Fact]
    public async Task Create_NonExistentDataSourceCode_Returns400()
    {
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportTask", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_Returns400()
    {
        string code = Guid.NewGuid().ToString();
        string dataSourceCode = await CreateDataSourceCodeAsync();
        HttpResponseMessage firstResponse = await HttpClient.PostAsJsonAsync("/ExportTask", NewRequest(code, dataSourceCode));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportTask", NewRequest(code, dataSourceCode));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingExportTask_ReturnsMatchingExportTask()
    {
        string dataSourceCode = await CreateDataSourceCodeAsync();
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), dataSourceCode);
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;

        ExportTaskResponse? fetched =
            await HttpClient.GetFromJsonAsync<ExportTaskResponse>($"/ExportTask/{created.Id}", JsonOptions);

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(request.Code, fetched.Code);
    }

    [Fact]
    public async Task GetById_NonExistentExportTask_Returns404()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/ExportTask/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AfterCreate_ContainsCreatedExportTask()
    {
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceCodeAsync());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;

        List<ExportTaskResponse>? all =
            await HttpClient.GetFromJsonAsync<List<ExportTaskResponse>>("/ExportTask", JsonOptions);

        Assert.NotNull(all);
        Assert.Contains(all!, x => x.Id == created.Id);
    }

    [Fact]
    public async Task Search_ByCode_FindsMatchingExportTask()
    {
        string code = Guid.NewGuid().ToString();
        await HttpClient.PostAsJsonAsync("/ExportTask", NewRequest(code, await CreateDataSourceCodeAsync()));

        List<ExportTaskResponse>? results = await HttpClient.GetFromJsonAsync<List<ExportTaskResponse>>(
            $"/ExportTask?code={code}", JsonOptions);

        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal(code, results![0].Code);
    }

    [Fact]
    public async Task Search_ByState_FindsOnlyMatchingState()
    {
        string dataSourceCode = await CreateDataSourceCodeAsync();
        await HttpClient.PostAsJsonAsync("/ExportTask", NewRequest(Guid.NewGuid().ToString(), dataSourceCode, TaskStateId.Ready));
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync(
            "/ExportTask", NewRequest(Guid.NewGuid().ToString(), dataSourceCode, TaskStateId.InProgress));
        ExportTaskResponse inProgress = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;

        List<ExportTaskResponse>? results = await HttpClient.GetFromJsonAsync<List<ExportTaskResponse>>(
            "/ExportTask?state=InProgress", JsonOptions);

        Assert.NotNull(results);
        Assert.Contains(results!, x => x.Id == inProgress.Id);
        Assert.All(results!, x => Assert.Equal(TaskStateId.InProgress, x.State));
    }

    [Fact]
    public async Task Update_ExistingExportTask_PersistsChangesAndPreservesLastStart()
    {
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceCodeAsync());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;
        // Postgres timestamptz truncates to microsecond precision, so the in-memory CreatedUtc from
        // the POST response has finer resolution than what a DB round-trip will return - fetch the
        // persisted baseline rather than comparing against it directly.
        ExportTaskResponse persistedBaseline =
            (await HttpClient.GetFromJsonAsync<ExportTaskResponse>($"/ExportTask/{created.Id}", JsonOptions))!;

        ExportTaskUpdateRequest updateRequest = AsUpdateRequest(created) with
        {
            DisplayName = "Updated Display Name",
            State = TaskStateId.InProgress,
            StateReason = "Running now",
            Parameter = new ExportTaskParameterRequest(
                Type: "Patient,Organization",
                Since: null,
                TypeFilterList: ["Patient", "Organization"]),
        };
        HttpResponseMessage updateResponse = await HttpClient.PutAsJsonAsync($"/ExportTask/{created.Id}", updateRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        ExportTaskResponse? updated = await updateResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Updated Display Name", updated!.DisplayName);
        Assert.Equal(TaskStateId.InProgress, updated.State);
        Assert.Equal(new[] { "Patient", "Organization" }, updated.Parameter.TypeFilterList);
        Assert.Equal(persistedBaseline.CreatedUtc, updated.CreatedUtc);
        Assert.Null(updated.LastStartUtc);
        Assert.True(updated.UpdatedUtc >= persistedBaseline.UpdatedUtc);
        // Code is not part of the update payload, and DataSourceCode is immutable once set - both must survive unchanged.
        Assert.Equal(created.Code, updated.Code);
        Assert.Equal(created.DataSourceCode, updated.DataSourceCode);

        // The PUT response reflects the tracked in-memory entity - fetch it back to prove the
        // change actually persisted to Postgres.
        ExportTaskResponse? fetched =
            await HttpClient.GetFromJsonAsync<ExportTaskResponse>($"/ExportTask/{created.Id}", JsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal("Updated Display Name", fetched!.DisplayName);
        Assert.Equal(new[] { "Patient", "Organization" }, fetched.Parameter.TypeFilterList);
    }

    [Fact]
    public async Task GetById_ResponseBody_CanBePutStraightBackWithoutModification()
    {
        // The whole point of ExportTaskUpdateRequest excluding the server-controlled fields
        // (Id, TypeId, Code, CreatedUtc, UpdatedUtc, LastStartUtc,
        // LastEndUtc) is that a client can round-trip a GET response straight back through PUT
        // without stripping anything out first - the extra JSON properties are just ignored. A
        // non-null Since is set here because the GET response converts it (and every other time
        // value) to ServiceDefaultTimeZone's offset (e.g. +10:00) - Npgsql rejects a non-UTC
        // DateTimeOffset for a "timestamp with time zone" column, so the PUT handler must normalise
        // it back to UTC before persisting rather than writing the round-tripped offset straight through.
        ExportTaskRequest request = NewRequest(
            Guid.NewGuid().ToString(),
            await CreateDataSourceCodeAsync(),
            since: DateTimeOffset.UtcNow.AddDays(-1));
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"/ExportTask/{created.Id}");
        string unmodifiedGetBody = await getResponse.Content.ReadAsStringAsync();
        // The service default time zone (+10:00 in test config) must actually be present on the
        // wire here - otherwise this test would not be exercising the offset-normalisation bug.
        Assert.Contains("+10:00", unmodifiedGetBody);

        HttpResponseMessage putResponse = await HttpClient.PutAsync(
            $"/ExportTask/{created.Id}",
            new StringContent(unmodifiedGetBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        ExportTaskResponse? updated = await putResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal(created.DisplayName, updated!.DisplayName);
        Assert.Equal(created.Code, updated.Code);
        Assert.Equal(created.DataSourceCode, updated.DataSourceCode);
        // Postgres timestamptz truncates to microsecond precision, so the in-memory Since carried on
        // created (never round-tripped through the DB) can be a fraction of a microsecond ahead of
        // updated's (read back after the PUT's SaveChanges) - assert the round-trip preserved the
        // same instant rather than bit-for-bit equality.
        Assert.NotNull(updated.Parameter.Since);
        Assert.True((created.Parameter.Since!.Value - updated.Parameter.Since!.Value).Duration() < TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Update_NonExistentExportTask_Returns404()
    {
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceCodeAsync());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;
        ExportTaskUpdateRequest updateRequest = AsUpdateRequest(created);

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync("/ExportTask/999999", updateRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingExportTask_Returns204ThenGetByIdReturns404()
    {
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceCodeAsync());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;

        HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"/ExportTask/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"/ExportTask/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistentExportTask_Returns404()
    {
        HttpResponseMessage response = await HttpClient.DeleteAsync("/ExportTask/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 8: Rename `ExportLoaderTaskMappingTests.cs` → `ExportTaskMappingTests.cs`**

Delete `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskMappingTests.cs`. Create
`src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportTaskMappingTests.cs`:

```csharp
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportTaskMappingTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    [Fact]
    public async Task SaveAndReload_ExportTask_RoundTripsOwnedParameterAndTypeFilterListArray()
    {
        DateTime nowUtc = DateTime.UtcNow;
        DataSource dataSource = new()
        {
            Code = Guid.NewGuid().ToString(),
            DisplayName = "Provider Connect Australia",
        };

        using (IServiceScope dataSourceScope = Fixture.Services.CreateScope())
        {
            ProviderDirectoryDbContext dataSourceContext =
                dataSourceScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
            dataSourceContext.DataSource.Add(dataSource);
            await dataSourceContext.SaveChangesAsync();
        }

        ExportTask task = new()
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
            DataSourceId = dataSource.Id,
            DataSource = dataSource,
            Parameter = new ExportParameter
            {
                Type = "Patient,Practitioner",
                Since = DateTimeOffset.UtcNow,
                TypeFilterList = ["Patient", "Practitioner"],
            },
        };

        using (IServiceScope writeScope = Fixture.Services.CreateScope())
        {
            ProviderDirectoryDbContext writeContext =
                writeScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
            // dataSource was saved and fetched through a different DbContext above, so this context's
            // change tracker doesn't know about it yet - attach it first so EF recognises it as
            // already-existing (its key is non-default) rather than re-inserting it as new.
            writeContext.Attach(dataSource);
            writeContext.ExportTasks.Add(task);
            await writeContext.SaveChangesAsync();
        }

        // A second, independent scope/DbContext forces a real read from Postgres rather than the
        // first-level change tracker cache.
        using IServiceScope readScope = Fixture.Services.CreateScope();
        ProviderDirectoryDbContext readContext =
            readScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
        ExportTask reloaded = await readContext.ExportTasks
            .AsNoTracking()
            .SingleAsync(x => x.Code == "bulk-import-au");

        Assert.Equal(task.DisplayName, reloaded.DisplayName);
        Assert.Equal(TaskStateId.Ready, reloaded.State);
        Assert.Equal(TaskTypeId.BulkImport, reloaded.TypeId);
        Assert.Equal(new[] { "Patient", "Practitioner" }, reloaded.Parameter.TypeFilterList);
        Assert.Equal(dataSource.Id, reloaded.DataSourceId);
    }
}
```

- [ ] **Step 9: Rename `ExportLoaderTaskRepositoryTests.cs` → `ExportTaskRepositoryTests.cs`**

Delete `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs`. Create
`src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportTaskRepositoryTests.cs` — the same CRUD/Search
tests Task 3 left in place (`ExportLoaderTaskRepositoryTests.cs` after its Step 9 deletion), retyped to
`ExportTask`/`IExportTaskRepository`:

```csharp
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportTaskRepositoryTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    private static ExportTask NewTask(
        string code,
        TaskStateId state = TaskStateId.Ready,
        DateTime? lastStart = null,
        TimeSpan? triggerEvery = null,
        DateTime? toStartAtUtc = null,
        DateTime? toEndAtUtc = null,
        int? dataSourceId = null,
        int failureCount = 0)
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new ExportTask
        {
            Code = code,
            DisplayName = $"Task {code}",
            Description = null,
            State = state,
            StateReason = null,
            TriggerEvery = triggerEvery ?? TimeSpan.FromHours(24),
            ToStartAtUtc = toStartAtUtc,
            ToEndAtUtc = toEndAtUtc,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = lastStart,
            LastEnd = null,
            FailureCount = failureCount,
            // When no existing DataSourceId is supplied, a fresh, unsaved DataSource is attached via
            // the navigation property - EF's graph tracking inserts it in the same SaveChanges call
            // that adds the task. Passing an existing id (the update-payload case) skips that: the
            // navigation is left null since only DataSourceId is read back off this transient object.
            DataSourceId = dataSourceId ?? 0,
            DataSource = dataSourceId is null
                ? new DataSource { Code = Guid.NewGuid().ToString(), DisplayName = "Test Data Source" }
                : null!,
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
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();

        ExportTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.True(added.Id > 0);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingTask_ReturnsMatchingTaskWithParameter()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ExportTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        ExportTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(added.Code, fetched!.Code);
        Assert.Equal(new[] { "Patient" }, fetched.Parameter.TypeFilterList);
    }

    [Fact]
    public async Task GetAllAsync_AfterAdd_ContainsAddedTask()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ExportTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        IReadOnlyList<ExportTask> all = await repository.GetAllAsync(CancellationToken.None);

        Assert.Contains(all, x => x.Id == added.Id);
    }

    [Fact]
    public async Task UpdateAsync_ExistingTask_PersistsChangesIncludingParameter()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ExportTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        ExportTask update = NewTask(added.Code, TaskStateId.InProgress, dataSourceId: added.DataSourceId);
        update.Parameter.TypeFilterList = ["Patient", "Organization"];
        // A fixed value clear of DateTime.UtcNow's sub-microsecond precision, so it round-trips
        // through the timestamptz column (microsecond precision) without truncation flakiness.
        DateTime newUpdatedUtc = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        update.UpdatedUtc = newUpdatedUtc;
        ExportTask? updated = await repository.UpdateAsync(added.Id, update, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(TaskStateId.InProgress, updated!.State);

        ExportTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
        Assert.Equal(new[] { "Patient", "Organization" }, fetched.Parameter.TypeFilterList);
        Assert.Equal(newUpdatedUtc, fetched.UpdatedUtc);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentTask_ReturnsNull()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();

        ExportTask? updated = await repository.UpdateAsync(999999, NewTask("missing"), CancellationToken.None);

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteAsync_ExistingTask_RemovesItThenGetByIdReturnsNull()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ExportTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        bool deleted = await repository.DeleteAsync(added.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await repository.GetByIdAsync(added.Id, CancellationToken.None));

        // The owned Parameter now lives in its own table rather than table-split into "task" - assert
        // the FK cascade actually removed its row too, not just that the parent is unreachable.
        ProviderDirectoryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
        long remainingParameterRows = await dbContext.Database
            .SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM export_task_parameter WHERE export_task_id = {added.Id}")
            .SingleAsync();
        Assert.Equal(0, remainingParameterRows);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentTask_ReturnsFalse()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();

        bool deleted = await repository.DeleteAsync(999999, CancellationToken.None);

        Assert.False(deleted);
    }

    [Fact]
    public async Task SearchAsync_WithNoFilters_ReturnsAllTasks()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ExportTask task1 = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
        ExportTask task2 = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
        ExportTask task3 = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        IReadOnlyList<ExportTask> results = await repository.SearchAsync(
            code: null,
            state: null,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: CancellationToken.None);

        Assert.Contains(results, x => x.Id == task1.Id);
        Assert.Contains(results, x => x.Id == task2.Id);
        Assert.Contains(results, x => x.Id == task3.Id);
    }

    [Fact]
    public async Task SearchAsync_ByCode_FindsMatchingTask()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        string code = Guid.NewGuid().ToString();
        await repository.AddAsync(NewTask(code), CancellationToken.None);

        IReadOnlyList<ExportTask> results = await repository.SearchAsync(
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
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        await repository.AddAsync(NewTask(Guid.NewGuid().ToString(), TaskStateId.Ready), CancellationToken.None);
        ExportTask inProgress = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), TaskStateId.InProgress), CancellationToken.None);

        IReadOnlyList<ExportTask> results = await repository.SearchAsync(
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
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        DateTime inRange = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        DateTime outOfRange = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime rangeFrom = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime rangeTo = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        ExportTask matching = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), lastStart: inRange), CancellationToken.None);
        ExportTask nonMatching = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), lastStart: outOfRange), CancellationToken.None);

        IReadOnlyList<ExportTask> results = await repository.SearchAsync(
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

- [ ] **Step 10: Update `TaskRepositoryTests.cs` (Task 2's file) to use `ExportTask`/`IExportTaskRepository`**

In `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskRepositoryTests.cs`, replace every
`ExportLoaderTask` with `ExportTask` and every `IExportLoaderTaskRepository` with
`IExportTaskRepository` (types only — method names, assertions, and `ITaskRepository`/`TaskBase` usage
are unchanged). Concretely: the `NewTask` helper's return type and `new ExportLoaderTask { ... }`
become `ExportTask`/`new ExportTask { ... }`; every test method's
`IExportLoaderTaskRepository exportLoaderTaskRepository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();`
becomes
`IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();`,
and every subsequent `exportLoaderTaskRepository.` call site in that method becomes
`exportTaskRepository.`. `ITaskRepository taskRepository` and all `taskRepository.` calls, and every
`Assert` line, are unchanged.

- [ ] **Step 11: Update `TaskSchedulerTests.cs` (Api.Tests) to use `ExportTask`/`IExportTaskRepository`**

In `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskSchedulerTests.cs`, replace every
`ExportLoaderTask` with `ExportTask` and every `IExportLoaderTaskRepository` with
`IExportTaskRepository` throughout (the `NewTask` helper's return type and body, and every test
method's `IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();`
line and its `ExportLoaderTask added = await repository.AddAsync(...)` /
`ExportLoaderTask? updated = await repository.GetByIdAsync(...)` lines). The
`Abm.PD.Core.Application.TaskScheduler` fully-qualified references from Task 4 are unchanged — the
namespace collision this works around has nothing to do with the entity rename.

- [ ] **Step 12: Full solution build**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: 0 errors across every project.

- [ ] **Step 13: Full solution test run**

Run: `dotnet test src/Abm.PD/Abm.PD.slnx`
Expected: every test passes — Domain/Repository unit tests, `Abm.PD.Core.Application.Tests`'s
in-memory-double tests, and `Abm.PD.Core.Api.Tests`'s Testcontainers-backed integration tests (Docker
must be running), including:
- `ExportTaskCrudTests` (all CRUD/search/route-shape cases against `/ExportTask`).
- `ExportTaskMappingTests` (owned-type round-trip).
- `ExportTaskRepositoryTests` (CRUD/Search only, scheduling methods removed in Task 3).
- `TaskRepositoryTests` (all 18 scheduling cases against `ITaskRepository`/`TaskBase`).
- `TaskSchedulerTests` in both projects, including
  `DoWork_DueReadyTask_PassesFullyLoadedTaskWithDataSourceToRunner` from Task 3 — this is the first run
  where it exercises the real `TaskRepository` (no `Include`) feeding `TaskScheduler`'s re-fetch through
  `ExportTaskRepository.GetByIdAsync`, end to end, against the freshly-regenerated schema from Task 6.

If anything fails here, it is the first real signal that something in Tasks 5-7's mechanical rename
was missed — cross-check the failing test's file against the corresponding step above before assuming
it's a new, unrelated bug.

- [ ] **Step 14: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Application.Tests/ExportRunnerTests.cs
git add src/Abm.PD/Abm.PD.Core.Application.Tests/TaskSchedulerTests.cs
git add src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryExportTaskRepository.cs
git add src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/ThrowingExportRunner.cs
git add src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/ScopeTrackingExportRunner.cs
git add src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportTaskCrudTests.cs
git add src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportTaskMappingTests.cs
git add src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportTaskRepositoryTests.cs
git add src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskRepositoryTests.cs
git add src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/TaskSchedulerTests.cs
git add src/Abm.PD/Abm.PD.Core.Api.Tests/TestDoubles/ConfigurableExportRunner.cs
git rm src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/InMemoryExportLoaderTaskRepository.cs
git rm src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskCrudTests.cs
git rm src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskMappingTests.cs
git rm src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs
git commit -m "$(cat <<'EOF'
Rename ExportLoaderTask to ExportTask across every test file

Completes the rename started two commits ago: every test double, fixture,
and test class now references ExportTask/IExportTaskRepository. First full
solution build and test run since the entity rename began - exercises the
regenerated migration end to end.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UMWQENpvJ4gbbbbtmhodF8
EOF
)"
```

---

## Final verification checklist

- [ ] `dotnet build src/Abm.PD/Abm.PD.slnx` — 0 errors, 0 warnings in `Abm.PD.Core.Api` (which
  `TreatWarningsAsErrors`).
- [ ] `dotnet test src/Abm.PD/Abm.PD.slnx` — all tests green.
- [ ] `git log --oneline` shows 7 commits, one per task above, each with a clear, scoped message.
- [ ] No stray `ExportLoaderTask` reference remains: `grep -r "ExportLoaderTask" src/Abm.PD --include=*.cs --include=*.json`
  returns nothing.
- [ ] `src/Abm.PD/Abm.PD.Core.Repository/Migrations/` contains exactly one migration pair
  (`<timestamp>_InitialCreate.cs` + `.Designer.cs`) plus `ProviderDirectoryDbContextModelSnapshot.cs`.
