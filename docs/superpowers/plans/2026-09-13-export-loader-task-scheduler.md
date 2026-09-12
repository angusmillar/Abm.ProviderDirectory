# ExportLoaderTask Scheduler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make due `ExportLoaderTask` rows actually run on their configured interval, with a still-running
execution guaranteed to block the next one — including across multiple `Abm.PD.Core.Api` replicas.

**Architecture:** A ported PyroServer tick engine (`Abm.Core.HostedService`) drives a new
`ExportLoaderTaskScheduler` on a short poll interval. Each tick reaps stale `InProgress` rows, finds due
tasks, and claims each with one atomic conditional `UPDATE` against Postgres (the cross-replica lock) before
running it through an extended `ExportRunner`/`IFhirBatchLoader`.

**Tech Stack:** .NET 10, EF Core (Npgsql) `ExecuteUpdateAsync`, ASP.NET Core `IHostedService`, xunit +
Testcontainers.PostgreSql (no mocking library — hand-rolled test doubles only).

**Spec:** `docs/superpowers/specs/2026-09-13-export-loader-task-scheduler-design.md`

## Global Constraints

- Target framework `net10.0`, `ImplicitUsings`/`Nullable` enabled on every project (per every existing
  `.csproj` in this solution).
- `Abm.PD.Core.Api` has `TreatWarningsAsErrors` — a warning there fails the build; the other projects in
  this plan do not.
- **No mocking library, ever.** Every test double in this plan is a small hand-written class implementing
  the real interface — matches every existing test project in this solution.
- **No test reaches the network.** `Abm.PD.Core.Api.Tests`' `IExportRunner` is replaced with a
  fully-in-process fake for every test in this plan; nothing here calls the live SIT server.
- Structured Serilog logging with named placeholders; Australian English in comments and log messages
  ("organised", "initialise").
- File-scoped namespaces; primary constructors for DI; explicit types in preference to `var`; multi-line
  method signatures with one parameter per line — match the surrounding file in every edit.
- New package reference versions in this plan use `10.0.11` (matching `Abm.Pyro.Application.csproj`'s own
  versions of the same packages, since the code being ported originates there).

---

## Task 1: Port PyroServer's `HostedServiceSupport` into `Abm.Core.HostedService`

**Files:**
- Create: `src/Abm.PD/Abm.Core/HostedService/ITimedHostedService.cs`
- Create: `src/Abm.PD/Abm.Core/HostedService/TimedHostedServiceManager.cs`
- Create: `src/Abm.PD/Abm.Core/HostedService/TimedHostedServiceManagerOptions.cs`
- Create: `src/Abm.PD/Abm.Core/HostedService/TimedHostedServiceManagerExtensions.cs`
- Create: `src/Abm.PD/Abm.Core/HostedService/IAppStartupService.cs`
- Create: `src/Abm.PD/Abm.Core/HostedService/AppStartupServiceManager.cs`
- Create: `src/Abm.PD/Abm.Core/HostedService/AppStartupServiceManagerExtensions.cs`
- Modify: `src/Abm.PD/Abm.Core/Abm.Core.csproj`
- Modify: `src/Abm.PD/Abm.Core.Tests/Abm.Core.Tests.csproj`
- Test: `src/Abm.PD/Abm.Core.Tests/HostedService/TimedHostedServiceManagerTests.cs`

**Interfaces:**
- Produces: `Abm.Core.HostedService.ITimedHostedService` (`Task DoWork(CancellationToken cancellationToken)`),
  `Abm.Core.HostedService.TimedHostedServiceManager<T>` (an `IHostedService`), `Abm.Core.HostedService.
  TimedHostedServiceManagerOptions<T>` (`TimeSpan TriggersEvery { get; set; }`, default 30s), and the
  `IServiceCollection.AddTimedHostedService<T>(Action<TimedHostedServiceManagerOptions<T>> configurator)`
  extension — Task 4 registers `ExportLoaderTaskScheduler` through this exact method.

- [ ] **Step 1: Copy the seven files verbatim, changing only the namespace**

Source: `C:\GitRepo\angusmillar\PyroServer\src\Abm.Pyro.Application\HostedServiceSupport\*.cs`.
Destination: `src/Abm.PD/Abm.Core/HostedService/*.cs` (same seven filenames).

In every copied file, change the namespace line from:

```csharp
namespace Abm.Pyro.Application.HostedServiceSupport;
```

to:

```csharp
namespace Abm.Core.HostedService;
```

No other line changes — the logic, XML doc comments, and formatting are unchanged. (There is no source
`using` of `Abm.Pyro.*` types in any of these seven files, so nothing else needs adjusting.)

- [ ] **Step 2: Add the two package references `Abm.Core.csproj` needs**

`Abm.Core.csproj` currently has only `Microsoft.Extensions.Options`. Add, inside the existing
`<ItemGroup>` that holds it:

```xml
<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.11" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.11" />
```

`Microsoft.Extensions.DependencyInjection.Abstractions` is not added explicitly — it arrives transitively
via `Hosting.Abstractions`, matching how `Abm.Pyro.Application.csproj` itself is set up (it lists the same
two packages plus `Options`, with no explicit DI.Abstractions reference either).

- [ ] **Step 3: Build to confirm the port compiles**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: succeeds, no errors from the new `Abm.Core.HostedService` files.

- [ ] **Step 4: Add DI/Logging packages to `Abm.Core.Tests.csproj` and write the non-overlap test**

`Abm.Core.Tests.csproj` currently references only `Abm.Core` and the xunit/Test.Sdk/coverlet packages —
add, inside its existing `<ItemGroup>` of package references:

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.11" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.11" />
```

Then create `src/Abm.PD/Abm.Core.Tests/HostedService/TimedHostedServiceManagerTests.cs`:

```csharp
using Abm.Core.HostedService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Abm.Core.Tests.HostedService;

public class TimedHostedServiceManagerTests
{
    private sealed class SlowTimedService(TimeSpan workDuration) : ITimedHostedService
    {
        private int _concurrentCalls;

        public int CallCount { get; private set; }
        public int MaxObservedConcurrency { get; private set; }

        public async Task DoWork(CancellationToken cancellationToken)
        {
            int concurrent = Interlocked.Increment(ref _concurrentCalls);
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, concurrent);
            CallCount++;
            await Task.Delay(workDuration, cancellationToken);
            Interlocked.Decrement(ref _concurrentCalls);
        }
    }

    [Fact]
    public async Task StartAsync_DoWorkSlowerThanInterval_NeverRunsConcurrentlyWithItself()
    {
        // DoWork (120ms) deliberately outlasts the tick interval (30ms) - this is exactly the
        // "still-running previous execution" scenario the whole scheduler design depends on the
        // tick engine handling correctly, in-process, before any DB-level claim is added on top.
        SlowTimedService slowService = new(TimeSpan.FromMilliseconds(120));
        ServiceCollection services = new();
        services.AddSingleton(slowService);
        await using ServiceProvider provider = services.BuildServiceProvider();

        TimedHostedServiceManagerOptions<SlowTimedService> options = new()
        {
            TriggersEvery = TimeSpan.FromMilliseconds(30),
        };
        TimedHostedServiceManager<SlowTimedService> manager = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TimedHostedServiceManager<SlowTimedService>>.Instance,
            options);

        await manager.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        await manager.StopAsync(CancellationToken.None);

        Assert.Equal(1, slowService.MaxObservedConcurrency);
        Assert.True(slowService.CallCount >= 2, $"Expected at least 2 ticks, got {slowService.CallCount}");
    }
}
```

- [ ] **Step 5: Run test to verify it fails first, then passes**

Run: `dotnet test src/Abm.PD/Abm.Core.Tests --filter TimedHostedServiceManagerTests`
Expected before Step 1/2 exist: does not compile (this is why Steps 1-2 came first here — the port itself
*is* the implementation for this task, so verify the test fails only in the sense of "would fail without
the port"; since the port already landed in Step 1, run this once and expect PASS. If it fails, the
`SlowTimedService`/timing assumptions need adjusting, not the ported code.)

- [ ] **Step 6: Commit**

```bash
git add src/Abm.PD/Abm.Core/HostedService src/Abm.PD/Abm.Core/Abm.Core.csproj src/Abm.PD/Abm.Core.Tests/Abm.Core.Tests.csproj src/Abm.PD/Abm.Core.Tests/HostedService
git commit -m "Port PyroServer's HostedServiceSupport tick engine into Abm.Core.HostedService"
```

---

## Task 2: Add claim/reap/due/outcome operations to `IExportLoaderTaskRepository`

**Files:**
- Modify: `src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportLoaderTaskRepository.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportLoaderTaskRepository.cs`
- Test: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs`

**Interfaces:**
- Consumes: existing `ExportLoaderTask` (`Id`, `State: TaskStateId`, `StateReason: string?`,
  `TriggerEvery: TimeSpan`, `ToStartAtUtc/ToEndAtUtc: DateTime?`, `LastStart/LastEnd: DateTime?`),
  `TaskStateId` (`Ready`, `InProgress`, `OnHold`, `Completed`, `Failed`), the test file's existing
  `NewTask(string code, TaskStateId state, DateTime? lastStart)` helper and `IntegrationTestFixture`/
  `IntegrationTestBase` (real Postgres via Testcontainers, reset per test).
- Produces: four new `IExportLoaderTaskRepository` members —
  `Task<IReadOnlyList<ExportLoaderTask>> FindDueAsync(DateTime nowUtc, CancellationToken ct)`,
  `Task<bool> TryClaimAsync(int id, DateTime nowUtc, CancellationToken ct)`,
  `Task ReapStaleInProgressAsync(DateTime olderThanUtc, CancellationToken ct)`,
  `Task RecordOutcomeAsync(int id, TaskStateId state, DateTime nowUtc, string? stateReason, CancellationToken ct)`
  — Task 4's `ExportLoaderTaskScheduler` calls exactly these four.

- [ ] **Step 1: Write the failing tests**

Extend `NewTask` in the existing test file with an optional `triggerEvery` and `toStartAtUtc`/`toEndAtUtc`
parameter (defaults keep every existing call site unchanged):

```csharp
private static ExportLoaderTask NewTask(
    string code,
    TaskStateId state = TaskStateId.Ready,
    DateTime? lastStart = null,
    TimeSpan? triggerEvery = null,
    DateTime? toStartAtUtc = null,
    DateTime? toEndAtUtc = null)
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
        Parameter = new ExportParameter
        {
            Type = "Patient",
            Since = null,
            TypeFilterList = ["Patient"],
        },
    };
}
```

Then append these `[Fact]`s to the same test class:

```csharp
[Fact]
public async Task FindDueAsync_TaskNeverRun_IsDue()
{
    using IServiceScope scope = Fixture.Services.CreateScope();
    IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
    ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

    IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(DateTime.UtcNow, CancellationToken.None);

    Assert.Contains(due, x => x.Id == added.Id);
}

[Fact]
public async Task FindDueAsync_LastStartPlusTriggerEveryInFuture_IsNotDue()
{
    using IServiceScope scope = Fixture.Services.CreateScope();
    IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
    ExportLoaderTask added = await repository.AddAsync(
        NewTask(Guid.NewGuid().ToString(), lastStart: DateTime.UtcNow, triggerEvery: TimeSpan.FromHours(1)),
        CancellationToken.None);

    IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(DateTime.UtcNow, CancellationToken.None);

    Assert.DoesNotContain(due, x => x.Id == added.Id);
}

[Fact]
public async Task FindDueAsync_BeforeToStartAtUtc_IsNotDue()
{
    using IServiceScope scope = Fixture.Services.CreateScope();
    IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
    DateTime now = DateTime.UtcNow;
    ExportLoaderTask added = await repository.AddAsync(
        NewTask(Guid.NewGuid().ToString(), toStartAtUtc: now.AddDays(1)), CancellationToken.None);

    IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(now, CancellationToken.None);

    Assert.DoesNotContain(due, x => x.Id == added.Id);
}

[Fact]
public async Task FindDueAsync_AfterToEndAtUtc_IsNotDue()
{
    using IServiceScope scope = Fixture.Services.CreateScope();
    IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
    DateTime now = DateTime.UtcNow;
    ExportLoaderTask added = await repository.AddAsync(
        NewTask(Guid.NewGuid().ToString(), toEndAtUtc: now.AddDays(-1)), CancellationToken.None);

    IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(now, CancellationToken.None);

    Assert.DoesNotContain(due, x => x.Id == added.Id);
}

[Fact]
public async Task FindDueAsync_InProgressTask_IsNotDue()
{
    using IServiceScope scope = Fixture.Services.CreateScope();
    IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
    ExportLoaderTask added = await repository.AddAsync(
        NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress), CancellationToken.None);

    IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(DateTime.UtcNow, CancellationToken.None);

    Assert.DoesNotContain(due, x => x.Id == added.Id);
}

[Fact]
public async Task TryClaimAsync_ReadyTask_ClaimsAndSetsInProgressAndLastStart()
{
    using IServiceScope scope = Fixture.Services.CreateScope();
    IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
    ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
    DateTime claimTime = DateTime.UtcNow;

    bool claimed = await repository.TryClaimAsync(added.Id, claimTime, CancellationToken.None);

    Assert.True(claimed);
    ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
    Assert.Equal(TaskStateId.InProgress, fetched!.State);
    Assert.Equal(claimTime, fetched.LastStart);
}

[Fact]
public async Task TryClaimAsync_AlreadyInProgressTask_ReturnsFalseAndLeavesLastStartUnchanged()
{
    using IServiceScope scope = Fixture.Services.CreateScope();
    IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
    DateTime originalLastStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    ExportLoaderTask added = await repository.AddAsync(
        NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress, lastStart: originalLastStart),
        CancellationToken.None);

    bool claimed = await repository.TryClaimAsync(added.Id, DateTime.UtcNow, CancellationToken.None);

    Assert.False(claimed);
    ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
    Assert.Equal(originalLastStart, fetched!.LastStart);
}

[Fact]
public async Task ReapStaleInProgressAsync_OlderThanCutoff_MovesToFailedWithReason()
{
    using IServiceScope scope = Fixture.Services.CreateScope();
    IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
    DateTime staleLastStart = DateTime.UtcNow.AddHours(-3);
    ExportLoaderTask added = await repository.AddAsync(
        NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress, lastStart: staleLastStart),
        CancellationToken.None);

    await repository.ReapStaleInProgressAsync(DateTime.UtcNow.AddHours(-2), CancellationToken.None);

    ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
    Assert.Equal(TaskStateId.Failed, fetched!.State);
    Assert.Equal("Reaped: exceeded expected run duration", fetched.StateReason);
}

[Fact]
public async Task ReapStaleInProgressAsync_NewerThanCutoff_IsLeftUnchanged()
{
    using IServiceScope scope = Fixture.Services.CreateScope();
    IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
    DateTime recentLastStart = DateTime.UtcNow.AddMinutes(-1);
    ExportLoaderTask added = await repository.AddAsync(
        NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress, lastStart: recentLastStart),
        CancellationToken.None);

    await repository.ReapStaleInProgressAsync(DateTime.UtcNow.AddHours(-2), CancellationToken.None);

    ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
    Assert.Equal(TaskStateId.InProgress, fetched!.State);
}

[Fact]
public async Task RecordOutcomeAsync_SetsStateStateReasonAndLastEnd()
{
    using IServiceScope scope = Fixture.Services.CreateScope();
    IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
    ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
    DateTime endTime = DateTime.UtcNow;

    await repository.RecordOutcomeAsync(added.Id, TaskStateId.Completed, endTime, "Committed 4 of 5, 1 failed", CancellationToken.None);

    ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
    Assert.Equal(TaskStateId.Completed, fetched!.State);
    Assert.Equal("Committed 4 of 5, 1 failed", fetched.StateReason);
    Assert.Equal(endTime, fetched.LastEnd);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests --filter ExportLoaderTaskRepositoryTests`
Expected: FAIL to compile — `IExportLoaderTaskRepository` has no `FindDueAsync`/`TryClaimAsync`/
`ReapStaleInProgressAsync`/`RecordOutcomeAsync` yet.

- [ ] **Step 3: Add the four members to the interface**

In `IExportLoaderTaskRepository.cs`, add after `SearchAsync`:

```csharp
    Task<IReadOnlyList<ExportLoaderTask>> FindDueAsync(
        DateTime nowUtc,
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
        CancellationToken cancellationToken);
```

- [ ] **Step 4: Implement them in `ExportLoaderTaskRepository`**

Add after `SearchAsync`:

```csharp
    public async Task<IReadOnlyList<ExportLoaderTask>> FindDueAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        return await dbContext.ExportLoaderTasks
            .AsNoTracking()
            .Where(t => t.State != TaskStateId.InProgress)
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
            .Where(t => t.Id == id && t.State != TaskStateId.InProgress)
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
                .SetProperty(t => t.StateReason, "Reaped: exceeded expected run duration"), cancellationToken);
    }

    public async Task RecordOutcomeAsync(
        int id,
        TaskStateId state,
        DateTime nowUtc,
        string? stateReason,
        CancellationToken cancellationToken)
    {
        await dbContext.ExportLoaderTasks
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, state)
                .SetProperty(t => t.LastEnd, nowUtc)
                .SetProperty(t => t.StateReason, stateReason), cancellationToken);
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests --filter ExportLoaderTaskRepositoryTests`
Expected: PASS, all facts including the pre-existing ones.

- [ ] **Step 6: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Domain/Repositories/IExportLoaderTaskRepository.cs src/Abm.PD/Abm.PD.Core.Repository/Repositories/ExportLoaderTaskRepository.cs src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskRepositoryTests.cs
git commit -m "Add claim/reap/due/outcome operations to IExportLoaderTaskRepository"
```

---

## Task 3: Extend `ExportRunner` to run a given `ExportLoaderTask` and actually load it

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Application.Tests/Abm.PD.Core.Application.Tests.csproj`
- Create: `src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/FakeFhirExporter.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/FakeFhirBatchLoader.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Application.Tests/FhirExportQueryTests.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Application.Tests/ExportRunnerTests.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application/Abm.PD.Core.Application.csproj`
- Modify: `src/Abm.PD/Abm.PD.Core.Application/IExportRunner.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application/ExportRunner.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application/FhirExportQuery.cs`

**Interfaces:**
- Consumes: `IFhirExporter.RequestDownloadManifest(Parameters, CancellationToken) : Task<FhirBulkExportManifest?>`,
  `IFhirExporter.StreamedExportFileList(CancellationToken) : IAsyncEnumerable<FhirBulkExportResource>`,
  `IFhirBatchLoader.Load(IAsyncEnumerable<FhirBulkExportResource>, CancellationToken) : Task<FhirBatchLoadResult>`,
  `FhirBulkExportResource(Resource Resource, string? ManifestOutputType, Uri SourceUrl, long LineNumber)`,
  `FhirBatchLoadResult(long SubmittedCount, long CommittedCount, long FailedCount, int BatchCount,
  IReadOnlyList<FhirBatchLoadFailure> RetainedFailures)`, `ExportLoaderTask.Parameter : ExportParameter`
  (`Type: string`, `Since: DateTimeOffset?`, `TypeFilterList: List<string>`).
- Produces: `IExportRunner.Run(ExportLoaderTask task, CancellationToken ct) : Task<FhirBatchLoadResult>`
  (replaces the old parameterless `Run(CancellationToken)`) — Task 4's `ExportLoaderTaskScheduler` calls
  this. `FhirExportQuery.FromParameter(ExportParameter parameter) : Parameters`.

- [ ] **Step 1: Add the `Abm.PD.Core.Domain` project reference `Abm.PD.Core.Application` needs**

`Abm.PD.Core.Application.csproj` currently references only `Abm.PD.BulkExport`. Add a second
`ProjectReference` inside its existing `<ItemGroup>`:

```xml
<ProjectReference Include="..\Abm.PD.Core.Domain\Abm.PD.Core.Domain.csproj" />
```

(Deliberately not `Abm.PD.Core.Repository` — that project's EF Core/`ProviderDirectoryDbContext`
specifics stay out of `Abm.PD.Core.Application`; this project only needs the entity type and the
repository *interface*, both of which live in `Abm.PD.Core.Domain`.)

- [ ] **Step 2: Create the new test project**

`src/Abm.PD/Abm.PD.Core.Application.Tests/Abm.PD.Core.Application.Tests.csproj`:

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
        <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.11" />
    </ItemGroup>

    <ItemGroup>
        <Using Include="Xunit" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\Abm.PD.Core.Application\Abm.PD.Core.Application.csproj" />
    </ItemGroup>

</Project>
```

Add it to the solution: `dotnet sln src/Abm.PD/Abm.PD.slnx add src/Abm.PD/Abm.PD.Core.Application.Tests/Abm.PD.Core.Application.Tests.csproj`

- [ ] **Step 3: Write the failing tests**

`src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/FakeFhirExporter.cs`:

```csharp
using System.Runtime.CompilerServices;
using Abm.PD.BulkExport;
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Models;
using Hl7.Fhir.Model;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

public sealed class FakeFhirExporter : IFhirExporter
{
    public FhirBulkExportManifest? ManifestToReturn { get; set; } = new FhirBulkExportManifest
    {
        TransactionTime = DateTimeOffset.UtcNow,
        RequiresAccessToken = false,
    };

    public List<FhirBulkExportResource> ResourcesToStream { get; set; } = [];

    public Parameters? ReceivedParameters { get; private set; }

    public Task<FhirBulkExportManifest?> RequestDownloadManifest(
        Parameters parameters,
        CancellationToken cancellationToken)
    {
        ReceivedParameters = parameters;
        return Task.FromResult(ManifestToReturn);
    }

    public async IAsyncEnumerable<FhirBulkExportResource> StreamedExportFileList(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (FhirBulkExportResource resource in ResourcesToStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return resource;
            await Task.Yield();
        }
    }
}
```

`src/Abm.PD/Abm.PD.Core.Application.Tests/TestDoubles/FakeFhirBatchLoader.cs`:

```csharp
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Loader;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

public sealed class FakeFhirBatchLoader : IFhirBatchLoader
{
    public List<FhirBulkExportResource> ReceivedResources { get; } = [];

    public FhirBatchLoadResult ResultToReturn { get; set; } =
        new(SubmittedCount: 0, CommittedCount: 0, FailedCount: 0, BatchCount: 0, RetainedFailures: []);

    public async Task<FhirBatchLoadResult> Load(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        CancellationToken cancellationToken)
    {
        await foreach (FhirBulkExportResource resource in exportResources.WithCancellation(cancellationToken))
        {
            ReceivedResources.Add(resource);
        }

        return ResultToReturn;
    }
}
```

`src/Abm.PD/Abm.PD.Core.Application.Tests/FhirExportQueryTests.cs`:

```csharp
using Abm.PD.Core.Domain.Entities;
using Hl7.Fhir.Model;

namespace Abm.PD.Core.Application.Tests;

public class FhirExportQueryTests
{
    [Fact]
    public void FromParameter_BuildsOutputFormatTypeAndTypeFilters()
    {
        ExportParameter parameter = new()
        {
            Type = "Practitioner,Organization",
            Since = null,
            TypeFilterList = ["Practitioner?active=true", "Organization?active=true"],
        };

        Parameters parameters = FhirExportQuery.FromParameter(parameter);

        Assert.Equal("application/fhir+ndjson", GetString(parameters, "_outputFormat"));
        Assert.Equal("Practitioner,Organization", GetString(parameters, "_type"));
        Assert.Equal(
            parameter.TypeFilterList,
            parameters.Parameter.Where(p => p.Name == "_typeFilter").Select(p => ((FhirString)p.Value).Value));
        Assert.DoesNotContain(parameters.Parameter, p => p.Name == "_since");
    }

    [Fact]
    public void FromParameter_WithSince_AddsSinceParameter()
    {
        DateTimeOffset since = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ExportParameter parameter = new() { Type = "Patient", Since = since, TypeFilterList = [] };

        Parameters parameters = FhirExportQuery.FromParameter(parameter);

        Instant sinceParam = Assert.IsType<Instant>(parameters.Parameter.Single(p => p.Name == "_since").Value);
        Assert.Equal(since, sinceParam.Value);
    }

    private static string GetString(Parameters parameters, string name) =>
        ((FhirString)parameters.Parameter.Single(p => p.Name == name).Value).Value;
}
```

`src/Abm.PD/Abm.PD.Core.Application.Tests/ExportRunnerTests.cs`:

```csharp
using Abm.PD.BulkExport.Loader;
using Abm.PD.Core.Application.Tests.TestDoubles;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace Abm.PD.Core.Application.Tests;

public class ExportRunnerTests
{
    private static ExportLoaderTask NewTask()
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new ExportLoaderTask
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
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    [Fact]
    public async Task Run_StreamsExportIntoBatchLoader_ReturnsLoadResult()
    {
        FakeFhirExporter fakeExporter = new();
        fakeExporter.ResourcesToStream.Add(new FhirBulkExportResource(
            Resource: new Patient { Id = "1" },
            ManifestOutputType: "Patient",
            SourceUrl: new Uri("https://export.test/Patient.ndjson"),
            LineNumber: 1));
        FakeFhirBatchLoader fakeLoader = new()
        {
            ResultToReturn = new FhirBatchLoadResult(
                SubmittedCount: 1, CommittedCount: 1, FailedCount: 0, BatchCount: 1, RetainedFailures: []),
        };
        ExportRunner runner = new(NullLogger<ExportRunner>.Instance, fakeExporter, fakeLoader);

        FhirBatchLoadResult result = await runner.Run(NewTask(), CancellationToken.None);

        Assert.Equal(1, result.CommittedCount);
        Assert.Single(fakeLoader.ReceivedResources);
        Assert.NotNull(fakeExporter.ReceivedParameters);
    }

    [Fact]
    public async Task Run_NullManifest_ThrowsArgumentNullException()
    {
        FakeFhirExporter fakeExporter = new() { ManifestToReturn = null };
        FakeFhirBatchLoader fakeLoader = new();
        ExportRunner runner = new(NullLogger<ExportRunner>.Instance, fakeExporter, fakeLoader);

        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.Run(NewTask(), CancellationToken.None));
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Application.Tests`
Expected: FAIL to compile — `FhirExportQuery.FromParameter` doesn't exist yet, and `IExportRunner.Run`/
`ExportRunner.Run` don't take an `ExportLoaderTask` yet.

- [ ] **Step 5: Add `FhirExportQuery.FromParameter`**

In `FhirExportQuery.cs`, add (needs `using Abm.PD.Core.Domain.Entities;` at the top of the file):

```csharp
    public static Parameters FromParameter(
        ExportParameter parameter)
    {
        Parameters parameters = new Parameters();
        parameters.Parameter.Add(new Parameters.ParameterComponent
        {
            Name = "_outputFormat",
            Value = new FhirString("application/fhir+ndjson"),
        });

        if (parameter.Since is not null)
        {
            parameters.Parameter.Add(new Parameters.ParameterComponent
            {
                Name = "_since",
                Value = new Instant { Value = parameter.Since },
            });
        }

        parameters.Parameter.Add(new Parameters.ParameterComponent
        {
            Name = "_type",
            Value = new FhirString(parameter.Type),
        });

        foreach (string typeFilter in parameter.TypeFilterList)
        {
            parameters.Parameter.Add(new Parameters.ParameterComponent
            {
                Name = "_typeFilter",
                Value = new FhirString(typeFilter),
            });
        }

        return parameters;
    }
```

- [ ] **Step 6: Change `IExportRunner`'s signature**

Replace the whole file `IExportRunner.cs`:

```csharp
using Abm.PD.BulkExport.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application;

public interface IExportRunner
{
    Task<FhirBatchLoadResult> Run(
        ExportLoaderTask task,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 7: Implement the new `ExportRunner.Run`**

Replace the whole file `ExportRunner.cs`:

```csharp
using Abm.PD.BulkExport;
using Abm.PD.BulkExport.Loader;
using Abm.PD.BulkExport.Models;
using Abm.PD.Core.Domain.Entities;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application;

public class ExportRunner(
    ILogger<ExportRunner> logger,
    IFhirExporter fhirExporter,
    IFhirBatchLoader fhirBatchLoader) : IExportRunner
{
    public async Task<FhirBatchLoadResult> Run(
        ExportLoaderTask task,
        CancellationToken cancellationToken)
    {
        Parameters parameters = FhirExportQuery.FromParameter(task.Parameter);

        FhirBulkExportManifest? fhirBulkExportManifest =
            await fhirExporter.RequestDownloadManifest(parameters, cancellationToken);

        ArgumentNullException.ThrowIfNull(fhirBulkExportManifest);

        logger.LogInformation("ExportLoaderTask {TaskCode} download manifest received, loading into target", task.Code);

        return await fhirBatchLoader.Load(
            exportResources: fhirExporter.StreamedExportFileList(cancellationToken),
            cancellationToken: cancellationToken);
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Application.Tests`
Expected: PASS.

- [ ] **Step 9: Build the whole solution to confirm nothing else broke**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: succeeds. (`Abm.PD.Console`/`ConsoleApplication.cs` never calls `IExportRunner`, so it is
unaffected by the signature change — confirm no other caller exists: `grep -rn "IExportRunner\|ExportRunner"
src/Abm.PD --include=*.cs` should show only `IExportRunner.cs`, `ExportRunner.cs`,
`DependencyInjection/ServiceCollectionExtension.cs`, and this task's own test files.)

- [ ] **Step 10: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Application.Tests src/Abm.PD/Abm.PD.Core.Application/Abm.PD.Core.Application.csproj src/Abm.PD/Abm.PD.Core.Application/IExportRunner.cs src/Abm.PD/Abm.PD.Core.Application/ExportRunner.cs src/Abm.PD/Abm.PD.Core.Application/FhirExportQuery.cs src/Abm.PD/Abm.PD.slnx
git commit -m "Extend ExportRunner to run a given ExportLoaderTask and load its export"
```

---

## Task 4: Add `ExportLoaderTaskScheduler`, wired into DI *and* safely into the test host

Registering `ExportLoaderTaskScheduler` as a real `IHostedService` means, from this task onward, every
`Abm.PD.Core.Api.Tests` run starts a real background timer against the shared Testcontainers database —
`IntegrationTestFixture` builds one `CoreApiWebApplicationFactory` for the whole test collection's
lifetime, so that timer would otherwise keep running, on its 30-second production default, across every
other test in the suite. Left unguarded even for one task/commit, it can make a real outbound FHIR call
(via the real `ExportRunner`) whenever another test happens to leave a due `ExportLoaderTask` row sitting
in the database between resets — precisely what this repo's testing rules forbid, and a source of
flaky, hard-to-diagnose cross-test state mutation. So the test-host safety net (the `IExportRunner`
fake and a much longer test `PollInterval`) is part of *this* task, not deferred to Task 5 — every task
in this plan must leave the repo's own test suite in a genuinely safe state.

**Files:**
- Create: `src/Abm.PD/Abm.PD.Core.Application/Settings/ExportLoaderTaskSchedulerSettings.cs`
- Create: `src/Abm.PD/Abm.PD.Core.Application/ExportLoaderTaskScheduler.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Application/DependencyInjection/ServiceCollectionExtension.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Api/appsettings.json`
- Create: `src/Abm.PD/Abm.PD.Core.Api.Tests/TestDoubles/ConfigurableExportRunner.cs`
- Modify: `src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/CoreApiWebApplicationFactory.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-3 — `Abm.Core.HostedService.ITimedHostedService`,
  `IExportLoaderTaskRepository.{FindDueAsync,TryClaimAsync,ReapStaleInProgressAsync,RecordOutcomeAsync}`,
  `IExportRunner.Run(ExportLoaderTask, CancellationToken) : Task<FhirBatchLoadResult>`,
  `Abm.Core.Time.IDateTimeProvider.Now : DateTimeOffset` (already registered as a singleton by
  `AddFhirBulkExportServices`, reachable from `Abm.PD.Core.Application` transitively via `Abm.PD.BulkExport`).
- Produces: `ExportLoaderTaskScheduler` (a `Scoped`, `ITimedHostedService`-implementing class),
  `ExportLoaderTaskSchedulerSettings` (`PollInterval`, `StaleInProgressAfter`), and
  `ConfigurableExportRunner` (test-only `IExportRunner` with a settable `Behaviour` delegate) — Task 5's
  tests resolve `ExportLoaderTaskScheduler` and `ConfigurableExportRunner` directly from DI.

- [ ] **Step 1: Add the settings record**

`src/Abm.PD/Abm.PD.Core.Application/Settings/ExportLoaderTaskSchedulerSettings.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Abm.PD.Core.Application.Settings;

public record ExportLoaderTaskSchedulerSettings
{
    public const string SectionName = "ExportLoaderTaskScheduler";

    /// <summary>
    /// How often the scheduler checks for due tasks. Independent of any task's own TriggerEvery - this
    /// is the poll granularity, not a schedule.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a task may sit InProgress before the scheduler assumes its runner crashed or was
    /// killed mid-run (no clean Completed/Failed update ever arrived) and reaps it back to Failed.
    /// Must comfortably exceed the slowest real export/load run, or a live task gets reaped out from
    /// under itself.
    /// </summary>
    [Range(typeof(TimeSpan), "00:05:00", "24:00:00")]
    public TimeSpan StaleInProgressAfter { get; init; } = TimeSpan.FromHours(2);
}
```

- [ ] **Step 2: Add the scheduler**

`src/Abm.PD/Abm.PD.Core.Application/ExportLoaderTaskScheduler.cs`:

```csharp
using Abm.Core.HostedService;
using Abm.Core.Time;
using Abm.PD.Core.Application.Settings;
using Abm.PD.BulkExport.Loader;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Abm.PD.Core.Application;

public class ExportLoaderTaskScheduler(
    IExportLoaderTaskRepository repository,
    IExportRunner exportRunner,
    IDateTimeProvider dateTimeProvider,
    IOptions<ExportLoaderTaskSchedulerSettings> settings,
    ILogger<ExportLoaderTaskScheduler> logger) : ITimedHostedService
{
    public async Task DoWork(
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = dateTimeProvider.Now.UtcDateTime;

        await repository.ReapStaleInProgressAsync(
            nowUtc - settings.Value.StaleInProgressAfter, cancellationToken);

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(nowUtc, cancellationToken);
        foreach (ExportLoaderTask task in due)
        {
            if (!await repository.TryClaimAsync(task.Id, nowUtc, cancellationToken))
            {
                // Another replica (or a human via the CRUD API) already claimed or changed this task
                // since FindDueAsync ran - this is the expected, silent outcome of losing the race.
                continue;
            }

            try
            {
                FhirBatchLoadResult result = await exportRunner.Run(task, cancellationToken);
                await repository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Completed,
                    dateTimeProvider.Now.UtcDateTime,
                    $"Committed {result.CommittedCount} of {result.SubmittedCount}, {result.FailedCount} failed",
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "ExportLoaderTask {TaskCode} failed", task.Code);
                await repository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Failed,
                    dateTimeProvider.Now.UtcDateTime,
                    exception.Message,
                    cancellationToken);
            }
        }
    }
}
```

- [ ] **Step 3: Wire it into DI**

Replace the whole file `ServiceCollectionExtension.cs`:

```csharp
using Abm.Core.HostedService;
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
        services.AddOptions<ExportLoaderTaskSchedulerSettings>()
            .Bind(configuration.GetSection(ExportLoaderTaskSchedulerSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // AddTimedHostedService<T>'s configurator runs synchronously at registration time, before the
        // host is built, so it cannot resolve IOptions<T> from the container the way the settings
        // above are read once the app starts - PollInterval is read straight off configuration here
        // instead, landing on the same bound value either way.
        ExportLoaderTaskSchedulerSettings schedulerSettings = configuration
            .GetSection(ExportLoaderTaskSchedulerSettings.SectionName)
            .Get<ExportLoaderTaskSchedulerSettings>() ?? new ExportLoaderTaskSchedulerSettings();

        services.AddScoped<IExportRunner, ExportRunner>();

        // AddTimedHostedService<T> already registers T (ExportLoaderTaskScheduler) as Scoped and adds
        // the IHostedService that ticks it - no separate AddScoped<ExportLoaderTaskScheduler>() call.
        services.AddTimedHostedService<ExportLoaderTaskScheduler>(opt =>
        {
            opt.TriggersEvery = schedulerSettings.PollInterval;
        });

        return services;
    }
}
```

- [ ] **Step 4: Document the new config section**

In `src/Abm.PD/Abm.PD.Core.Api/appsettings.json`, add a top-level section (matching the existing
`"Database": { ... }` section's style):

```json
  "ExportLoaderTaskScheduler": {
    "PollInterval": "00:00:30",
    "StaleInProgressAfter": "02:00:00"
  },
```

- [ ] **Step 5: Add the configurable `IExportRunner` fake for tests**

`src/Abm.PD/Abm.PD.Core.Api.Tests/TestDoubles/ConfigurableExportRunner.cs`:

```csharp
using Abm.PD.BulkExport.Loader;
using Abm.PD.Core.Application;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Api.Tests.TestDoubles;

/// <summary>
/// A per-test-configurable IExportRunner substituted for the real one in CoreApiWebApplicationFactory,
/// so ExportLoaderTaskScheduler never makes a real FHIR HTTP call during any Abm.PD.Core.Api.Tests run.
/// Registered as a singleton - tests within the shared IntegrationTestCollection run sequentially, so
/// setting Behaviour per test is safe.
/// </summary>
public sealed class ConfigurableExportRunner : IExportRunner
{
    public Func<ExportLoaderTask, CancellationToken, Task<FhirBatchLoadResult>>? Behaviour { get; set; }

    public Task<FhirBatchLoadResult> Run(
        ExportLoaderTask task,
        CancellationToken cancellationToken)
    {
        if (Behaviour is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ConfigurableExportRunner)}.{nameof(Behaviour)} was not set before the scheduler ran.");
        }

        return Behaviour(task, cancellationToken);
    }
}
```

- [ ] **Step 6: Replace `IExportRunner` and tame the scheduler's own timer in the test factory**

Replace the whole file `CoreApiWebApplicationFactory.cs`:

```csharp
using Abm.PD.Core.Api.Tests.TestDoubles;
using Abm.PD.Core.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

                // Long enough that the scheduler's own background timer never ticks during a test run -
                // IntegrationTestFixture builds one factory for the whole test collection's lifetime, so
                // without this the real 30-second production default would keep firing in the background
                // across every other test in the suite.
                ["ExportLoaderTaskScheduler:PollInterval"] = "01:00:00",
                // The minimum this repo's settings validation allows - short enough that a reaped-task
                // test only needs a LastStart a few minutes in the past, not the 2-hour production default.
                ["ExportLoaderTaskScheduler:StaleInProgressAfter"] = "00:05:00",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Swaps the real ExportRunner (which would otherwise make real FHIR HTTP calls) for a
            // per-test-configurable fake, for the lifetime of this factory.
            services.RemoveAll<IExportRunner>();
            services.AddSingleton<ConfigurableExportRunner>();
            services.AddSingleton<IExportRunner>(sp => sp.GetRequiredService<ConfigurableExportRunner>());
        });
    }
}
```

- [ ] **Step 7: Build and run the existing suite to verify the test host is safe**

Run: `dotnet build src/Abm.PD/Abm.PD.slnx`
Expected: succeeds.

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests --filter ExportLoaderTaskRepositoryTests`
Expected: PASS. This confirms `ExportLoaderTaskScheduler` is registered and ticking in the background
(as a real `IHostedService`) against the test host, but every tick's `IExportRunner` call is answered by
`ConfigurableExportRunner` (which throws if unconfigured, per Step 5 — a background tick hitting a due
task left by another test with no `Behaviour` set would throw *inside that tick*, not fail the test, since
`ExportLoaderTaskScheduler.DoWork` catches per-task exceptions; this is acceptable noise, not a real
network call).

- [ ] **Step 8: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Application/Settings src/Abm.PD/Abm.PD.Core.Application/ExportLoaderTaskScheduler.cs src/Abm.PD/Abm.PD.Core.Application/DependencyInjection/ServiceCollectionExtension.cs src/Abm.PD/Abm.PD.Core.Api/appsettings.json src/Abm.PD/Abm.PD.Core.Api.Tests/TestDoubles/ConfigurableExportRunner.cs src/Abm.PD/Abm.PD.Core.Api.Tests/Fixtures/CoreApiWebApplicationFactory.cs
git commit -m "Add ExportLoaderTaskScheduler, wire it into DI, and stub it safely in the test host"
```

---

## Task 5: Test the scheduler's claim/reap/run/record behaviour end-to-end

**Files:**
- Test: `src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskSchedulerTests.cs`

**Interfaces:**
- Consumes: `ExportLoaderTaskScheduler.DoWork(CancellationToken) : Task`, `IExportLoaderTaskRepository`
  (Task 2), `IExportRunner.Run(ExportLoaderTask, CancellationToken) : Task<FhirBatchLoadResult>` and
  `ConfigurableExportRunner` (both Task 4), `IntegrationTestFixture`/`IntegrationTestBase` (existing).

- [ ] **Step 1: Write the failing tests**

`src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskSchedulerTests.cs`:

```csharp
using Abm.PD.BulkExport.Loader;
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Api.Tests.TestDoubles;
using Abm.PD.Core.Application;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportLoaderTaskSchedulerTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    private static ExportLoaderTask NewTask(
        TaskStateId state = TaskStateId.Ready,
        DateTime? lastStart = null,
        TimeSpan? triggerEvery = null)
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
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    [Fact]
    public async Task DoWork_DueReadyTask_ClaimsRunsAndRecordsCompleted()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        ExportLoaderTaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(), CancellationToken.None);
        exportRunner.Behaviour = (_, _) => Task.FromResult(
            new FhirBatchLoadResult(SubmittedCount: 5, CommittedCount: 4, FailedCount: 1, BatchCount: 1, RetainedFailures: []));

        await scheduler.DoWork(CancellationToken.None);

        ExportLoaderTask? updated = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(TaskStateId.Completed, updated!.State);
        Assert.NotNull(updated.LastEnd);
        Assert.Equal("Committed 4 of 5, 1 failed", updated.StateReason);
    }

    [Fact]
    public async Task DoWork_RunnerThrows_RecordsFailedWithExceptionMessage()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        ExportLoaderTaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(), CancellationToken.None);
        exportRunner.Behaviour = (_, _) => throw new InvalidOperationException("SIT server unreachable");

        await scheduler.DoWork(CancellationToken.None);

        ExportLoaderTask? updated = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(TaskStateId.Failed, updated!.State);
        Assert.Equal("SIT server unreachable", updated.StateReason);
    }

    [Fact]
    public async Task DoWork_TaskNotYetDue_IsNeverClaimed()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        ExportLoaderTaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(lastStart: DateTime.UtcNow, triggerEvery: TimeSpan.FromHours(1)), CancellationToken.None);
        bool wasCalled = false;
        exportRunner.Behaviour = (_, _) =>
        {
            wasCalled = true;
            return Task.FromResult(new FhirBatchLoadResult(0, 0, 0, 0, []));
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
        ExportLoaderTaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();
        // CoreApiWebApplicationFactory sets StaleInProgressAfter to 5 minutes for tests - 10 minutes
        // stale is comfortably past that without needing to wait in real time.
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(state: TaskStateId.InProgress, lastStart: DateTime.UtcNow.AddMinutes(-10)), CancellationToken.None);
        exportRunner.Behaviour = (_, _) => Task.FromResult(new FhirBatchLoadResult(0, 0, 0, 0, []));

        await scheduler.DoWork(CancellationToken.None);

        ExportLoaderTask? reaped = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(reaped);
        Assert.Equal(TaskStateId.Failed, reaped!.State);
        Assert.Equal("Reaped: exceeded expected run duration", reaped.StateReason);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail, then pass**

Run: `dotnet test src/Abm.PD/Abm.PD.Core.Api.Tests --filter ExportLoaderTaskSchedulerTests`
Expected: FAIL to compile before this file exists. Once added: PASS — `ConfigurableExportRunner` and
`CoreApiWebApplicationFactory`'s override (both already in place from Task 4) are exactly what makes
`Behaviour` settable per test. If any fact fails, check first whether the factory's `RemoveAll`/
`AddSingleton` ordering actually wins over the app's own `AddScoped<IExportRunner, ExportRunner>()` —
`ConfigureTestServices` runs after `Program.cs`'s service registrations, so the replacement should always
apply.

- [ ] **Step 3: Run the whole test suite to confirm no regressions**

Run: `dotnet test src/Abm.PD/Abm.PD.slnx`
Expected: PASS — every test project, including the pre-existing `ExportLoaderTaskRepositoryTests` facts
and every new test added across this plan.

- [ ] **Step 4: Commit**

```bash
git add src/Abm.PD/Abm.PD.Core.Api.Tests/ExportLoaderTasks/ExportLoaderTaskSchedulerTests.cs
git commit -m "Test ExportLoaderTaskScheduler's claim/reap/run/record behaviour end-to-end"
```
