using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Api.Tests.TestDoubles;
using Abm.PD.Core.Application;
using Abm.PD.Core.Application.Loader;
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
        ExportLoaderTaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();
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
        ExportLoaderTaskScheduler scheduler = scope.ServiceProvider.GetRequiredService<ExportLoaderTaskScheduler>();
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
    }
}
