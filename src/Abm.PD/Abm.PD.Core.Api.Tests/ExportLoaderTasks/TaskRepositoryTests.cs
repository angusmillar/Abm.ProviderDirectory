using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class TaskRepositoryTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    private static ExportTask NewTask(
        string code,
        TaskStateId state = TaskStateId.Ready,
        DateTime? lastStart = null,
        TimeSpan? triggerEvery = null,
        DateTime? toStartAtUtc = null,
        DateTime? toEndAtUtc = null,
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
            DataSourceId = 0,
            DataSource = new DataSource { Code = Guid.NewGuid().ToString(), DisplayName = "Test Data Source" },
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    [Fact]
    public async Task FindDueAsync_TaskNeverRun_IsDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.Contains(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_LastStartPlusTriggerEveryInFuture_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), lastStart: DateTime.UtcNow, triggerEvery: TimeSpan.FromHours(1)),
            CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_BeforeToStartAtUtc_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        DateTime now = DateTime.UtcNow;
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), toStartAtUtc: now.AddDays(1)), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(now, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_AfterToEndAtUtc_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        DateTime now = DateTime.UtcNow;
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), toEndAtUtc: now.AddDays(-1)), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(now, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_InProgressTask_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_OnHoldTask_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.OnHold), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_FailedTaskExceedingFailureAttemptCount_IsNotDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 4),
            CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_FailedTaskWithinFailureAttemptCount_IsDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 3),
            CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.Contains(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task FindDueAsync_CompletedTaskPastTriggerEvery_IsDue()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(
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
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), triggerEvery: TimeSpan.Zero), CancellationToken.None);

        IReadOnlyList<TaskBase> due = await taskRepository.FindDueAsync(DateTime.UtcNow, failureAttemptCount: 3, CancellationToken.None);

        Assert.DoesNotContain(due, x => x.Id == added.Id);
    }

    [Fact]
    public async Task TryClaimAsync_ReadyTask_ClaimsAndSetsInProgressAndLastStart()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
        DateTime claimTime = new(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);

        bool claimed = await taskRepository.TryClaimAsync(added.Id, claimTime, CancellationToken.None);

        Assert.True(claimed);
        ExportTask? fetched = await exportTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
        Assert.Equal(claimTime, fetched.LastStart);
    }

    [Fact]
    public async Task TryClaimAsync_FailedTask_ClaimsAndSetsInProgress()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 1), CancellationToken.None);

        bool claimed = await taskRepository.TryClaimAsync(added.Id, DateTime.UtcNow, CancellationToken.None);

        Assert.True(claimed);
        ExportTask? fetched = await exportTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
    }

    [Fact]
    public async Task TryClaimAsync_AlreadyInProgressTask_ReturnsFalseAndLeavesLastStartUnchanged()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        DateTime originalLastStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress, lastStart: originalLastStart),
            CancellationToken.None);

        bool claimed = await taskRepository.TryClaimAsync(added.Id, DateTime.UtcNow, CancellationToken.None);

        Assert.False(claimed);
        ExportTask? fetched = await exportTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(originalLastStart, fetched!.LastStart);
    }

    [Fact]
    public async Task ReapStaleInProgressAsync_OlderThanCutoff_MovesToFailedWithReason()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        DateTime staleLastStart = DateTime.UtcNow.AddHours(-3);
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress, lastStart: staleLastStart),
            CancellationToken.None);

        await taskRepository.ReapStaleInProgressAsync(DateTime.UtcNow.AddHours(-2), CancellationToken.None);

        ExportTask? fetched = await exportTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.Failed, fetched!.State);
        Assert.Equal("Reaped: exceeded expected run duration", fetched.StateReason);
        Assert.Equal(1, fetched.FailureCount);
    }

    [Fact]
    public async Task ReapStaleInProgressAsync_NewerThanCutoff_IsLeftUnchanged()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        DateTime recentLastStart = DateTime.UtcNow.AddMinutes(-1);
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.InProgress, lastStart: recentLastStart),
            CancellationToken.None);

        await taskRepository.ReapStaleInProgressAsync(DateTime.UtcNow.AddHours(-2), CancellationToken.None);

        ExportTask? fetched = await exportTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
    }

    [Fact]
    public async Task RecordOutcomeAsync_SetsStateStateReasonAndLastEnd()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
        DateTime endTime = new(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);

        await taskRepository.RecordOutcomeAsync(
            added.Id, TaskStateId.Completed, endTime, "Committed 4 of 5, 1 failed", FailureCountUpdate.Unchanged, CancellationToken.None);

        ExportTask? fetched = await exportTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.Completed, fetched!.State);
        Assert.Equal("Committed 4 of 5, 1 failed", fetched.StateReason);
        Assert.Equal(endTime, fetched.LastEnd);
    }

    [Fact]
    public async Task RecordOutcomeAsync_ResetFailureCount_SetsFailureCountToZero()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), state: TaskStateId.Failed, failureCount: 2), CancellationToken.None);

        await taskRepository.RecordOutcomeAsync(
            added.Id, TaskStateId.Completed, DateTime.UtcNow, "ok", FailureCountUpdate.Reset, CancellationToken.None);

        ExportTask? fetched = await exportTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(0, fetched!.FailureCount);
    }

    [Fact]
    public async Task RecordOutcomeAsync_IncrementFailureCount_AddsOneToFailureCount()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository exportTaskRepository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ITaskRepository taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        ExportTask added = await exportTaskRepository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), failureCount: 1), CancellationToken.None);

        await taskRepository.RecordOutcomeAsync(
            added.Id, TaskStateId.Failed, DateTime.UtcNow, "boom", FailureCountUpdate.Increment, CancellationToken.None);

        ExportTask? fetched = await exportTaskRepository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(2, fetched!.FailureCount);
    }
}
