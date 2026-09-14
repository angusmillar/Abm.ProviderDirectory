using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportLoaderTaskRepositoryTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    private static ExportLoaderTask NewTask(
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
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();

        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.True(added.Id > 0);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingTask_ReturnsMatchingTaskWithParameter()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
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
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> all = await repository.GetAllAsync(CancellationToken.None);

        Assert.Contains(all, x => x.Id == added.Id);
    }

    [Fact]
    public async Task UpdateAsync_ExistingTask_PersistsChangesIncludingParameter()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        ExportLoaderTask update = NewTask(added.Code, TaskStateId.InProgress, dataSourceId: added.DataSourceId);
        update.Parameter.TypeFilterList = ["Patient", "Organization"];
        // A fixed value clear of DateTime.UtcNow's sub-microsecond precision, so it round-trips
        // through the timestamptz column (microsecond precision) without truncation flakiness.
        DateTime newUpdatedUtc = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        update.UpdatedUtc = newUpdatedUtc;
        ExportLoaderTask? updated = await repository.UpdateAsync(added.Id, update, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(TaskStateId.InProgress, updated!.State);

        ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
        Assert.Equal(new[] { "Patient", "Organization" }, fetched.Parameter.TypeFilterList);
        Assert.Equal(newUpdatedUtc, fetched.UpdatedUtc);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentTask_ReturnsNull()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();

        ExportLoaderTask? updated = await repository.UpdateAsync(999999, NewTask("missing"), CancellationToken.None);

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteAsync_ExistingTask_RemovesItThenGetByIdReturnsNull()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        bool deleted = await repository.DeleteAsync(added.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await repository.GetByIdAsync(added.Id, CancellationToken.None));

        // The owned Parameter now lives in its own table rather than table-split into "task" - assert
        // the FK cascade actually removed its row too, not just that the parent is unreachable.
        ProviderDirectoryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
        long remainingParameterRows = await dbContext.Database
            .SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM export_loader_task_parameter WHERE export_loader_task_id = {added.Id}")
            .SingleAsync();
        Assert.Equal(0, remainingParameterRows);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentTask_ReturnsFalse()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();

        bool deleted = await repository.DeleteAsync(999999, CancellationToken.None);

        Assert.False(deleted);
    }

    [Fact]
    public async Task SearchAsync_WithNoFilters_ReturnsAllTasks()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask task1 = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
        ExportLoaderTask task2 = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
        ExportLoaderTask task3 = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> results = await repository.SearchAsync(
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
        using IServiceScope scope = Fixture.Services.CreateScope();
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
        using IServiceScope scope = Fixture.Services.CreateScope();
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

    [Fact]
    public async Task FindDueAsync_TaskNeverRun_IsDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

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

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

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

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(now, failureAttemptCount: 3, CancellationToken.None);

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

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(now, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_InProgressTask_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_OnHoldTask_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.OnHold), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_FailedTaskExceedingFailureAttemptCount_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 4),
            CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_FailedTaskWithinFailureAttemptCount_IsDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 3),
            CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.Contains(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_CompletedTaskPastTriggerEvery_IsDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(
                Guid.NewGuid().ToString(),
                state: TaskStateId.Completed,
                lastStart: DateTime.UtcNow.AddHours(-2),
                triggerEvery: TimeSpan.FromHours(1)),
            CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.Contains(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_ZeroTriggerEvery_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), triggerEvery: TimeSpan.Zero), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task TryClaimAsync_ReadyTask_ClaimsAndSetsInProgressAndLastStart()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
        // Truncated to microsecond precision - Postgres timestamptz stores microseconds, not the
        // 100ns ticks DateTime.UtcNow carries, so an untruncated value round-trips lossily.
        DateTime claimTime = new(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);

        bool claimed = await repository.TryClaimAsync(added.Id, claimTime, CancellationToken.None);

        Assert.True(claimed);
        ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
        Assert.Equal(claimTime, fetched.LastStart);
    }

    [Fact]
    public async Task TryClaimAsync_FailedTask_ClaimsAndSetsInProgress()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 1), CancellationToken.None);

        bool claimed = await repository.TryClaimAsync(added.Id, DateTime.UtcNow, CancellationToken.None);

        Assert.True(claimed);
        ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
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
        Assert.Equal(1, fetched.FailureCount);
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
        // Truncated to microsecond precision - Postgres timestamptz stores microseconds, not the
        // 100ns ticks DateTime.UtcNow carries, so an untruncated value round-trips lossily.
        DateTime endTime = new(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);

        await repository.RecordOutcomeAsync(
            added.Id, TaskStateId.Completed, endTime, "Committed 4 of 5, 1 failed", FailureCountUpdate.Unchanged, CancellationToken.None);

        ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.Completed, fetched!.State);
        Assert.Equal("Committed 4 of 5, 1 failed", fetched.StateReason);
        Assert.Equal(endTime, fetched.LastEnd);
    }

    [Fact]
    public async Task RecordOutcomeAsync_ResetFailureCount_SetsFailureCountToZero()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 2), CancellationToken.None);

        await repository.RecordOutcomeAsync(
            added.Id, TaskStateId.Completed, DateTime.UtcNow, "ok", FailureCountUpdate.Reset, CancellationToken.None);

        ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(0, fetched!.FailureCount);
    }

    [Fact]
    public async Task RecordOutcomeAsync_IncrementFailureCount_AddsOneToFailureCount()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), failureCount: 1), CancellationToken.None);

        await repository.RecordOutcomeAsync(
            added.Id, TaskStateId.Failed, DateTime.UtcNow, "boom", FailureCountUpdate.Increment, CancellationToken.None);

        ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(2, fetched!.FailureCount);
    }
}
