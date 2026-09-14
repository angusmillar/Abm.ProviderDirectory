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

    private static ExportTask NewTask(
        TaskStateId state = TaskStateId.Ready,
        DateTime? lastStart = null,
        TimeSpan? triggerEvery = null,
        int failureCount = 0)
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new ExportTask
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
        IExportTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        ExportTask added = await repository.AddAsync(NewTask(), CancellationToken.None);
        exportRunner.Behaviour = (_, _) => Task.FromResult(
            new SourceResourceLoadResult(SubmittedCount: 5, CommittedCount: 4, FailedCount: 1, BatchCount: 1, RetainedFailures: []));

        await scheduler.DoWork(CancellationToken.None);

        ExportTask? updated = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(TaskStateId.Completed, updated!.State);
        Assert.NotNull(updated.LastEnd);
        Assert.Equal("Persisted 4 of 5, 1 failed", updated.StateReason);
    }

    [Fact]
    public async Task DoWork_DueReadyTask_PassesFullyLoadedTaskWithDataSourceToRunner()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        ExportTask added = await repository.AddAsync(NewTask(), CancellationToken.None);
        ExportTask? receivedTask = null;
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
        IExportTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        ExportTask added = await repository.AddAsync(NewTask(), CancellationToken.None);
        exportRunner.Behaviour = (_, _) => throw new InvalidOperationException("SIT server unreachable");

        await scheduler.DoWork(CancellationToken.None);

        ExportTask? updated = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(TaskStateId.Failed, updated!.State);
        Assert.Equal("SIT server unreachable", updated.StateReason);
        Assert.Equal(1, updated.FailureCount);
    }

    [Fact]
    public async Task DoWork_TaskNotYetDue_IsNeverClaimed()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        ExportTask added = await repository.AddAsync(
            NewTask(lastStart: DateTime.UtcNow, triggerEvery: TimeSpan.FromHours(1)), CancellationToken.None);
        bool wasCalled = false;
        exportRunner.Behaviour = (_, _) =>
        {
            wasCalled = true;
            return Task.FromResult(new SourceResourceLoadResult(0, 0, 0, 0, []));
        };

        await scheduler.DoWork(CancellationToken.None);

        Assert.False(wasCalled);
        ExportTask? unchanged = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.Ready, unchanged!.State);
    }

    [Fact]
    public async Task DoWork_StaleInProgressTask_IsReapedToFailed()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        // CoreApiWebApplicationFactory sets StaleInProgressAfter to 5 minutes for tests - 10 minutes
        // stale is comfortably past that without needing to wait in real time.
        ExportTask added = await repository.AddAsync(
            NewTask(state: TaskStateId.InProgress, lastStart: DateTime.UtcNow.AddMinutes(-10)), CancellationToken.None);
        exportRunner.Behaviour = (_, _) => Task.FromResult(new SourceResourceLoadResult(0, 0, 0, 0, []));

        await scheduler.DoWork(CancellationToken.None);

        ExportTask? reaped = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(reaped);
        Assert.Equal(TaskStateId.Failed, reaped!.State);
        Assert.Equal("Reaped: exceeded expected run duration", reaped.StateReason);
        Assert.Equal(1, reaped.FailureCount);
    }

    [Fact]
    public async Task DoWork_FailedTaskWithinFailureAttemptCount_IsClaimedAndRun()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        // CoreApiWebApplicationFactory leaves FailureAttemptCount at its default of 3, so a Failed
        // task carrying FailureCount 3 is still Due.
        ExportTask added = await repository.AddAsync(
            NewTask(state: TaskStateId.Failed, failureCount: 3), CancellationToken.None);
        bool wasCalled = false;
        exportRunner.Behaviour = (_, _) =>
        {
            wasCalled = true;
            return Task.FromResult(new SourceResourceLoadResult(0, 0, 0, 0, []));
        };

        await scheduler.DoWork(CancellationToken.None);

        Assert.True(wasCalled);
        ExportTask? updated = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.Completed, updated!.State);
        Assert.Equal(0, updated.FailureCount);
    }

    [Fact]
    public async Task DoWork_FailedTaskExceedingFailureAttemptCount_IsNeverClaimed()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportTaskRepository repository = scope.ServiceProvider.GetRequiredService<IExportTaskRepository>();
        ConfigurableExportRunner exportRunner = scope.ServiceProvider.GetRequiredService<ConfigurableExportRunner>();
        Abm.PD.Core.Application.TaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<Abm.PD.Core.Application.TaskScheduler>();
        ExportTask added = await repository.AddAsync(
            NewTask(state: TaskStateId.Failed, failureCount: 4), CancellationToken.None);
        bool wasCalled = false;
        exportRunner.Behaviour = (_, _) =>
        {
            wasCalled = true;
            return Task.FromResult(new SourceResourceLoadResult(0, 0, 0, 0, []));
        };

        await scheduler.DoWork(CancellationToken.None);

        Assert.False(wasCalled);
        ExportTask? unchanged = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.Equal(TaskStateId.Failed, unchanged!.State);
        Assert.Equal(4, unchanged.FailureCount);
    }
}
