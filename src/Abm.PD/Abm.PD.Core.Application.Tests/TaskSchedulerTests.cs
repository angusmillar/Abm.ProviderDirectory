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

        public DateTimeOffset ToServiceOffset(
            DateTime utcDateTime)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc)).ToOffset(ServiceDefaultTimeZone);
        }

        public DateTimeOffset? ToServiceOffset(
            DateTime? utcDateTime)
        {
            if (utcDateTime == null)
            {
                return null;
            }
            return ToServiceOffset(utcDateTime.Value);
        }

        public TimeSpan ServiceDefaultTimeZone { get; } = TimeSpan.FromHours(10);
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

    // Regression test: each due task must get its own DI scope, and therefore its own IExportRunner
    // (and the scoped IFhirExporter/IFhirBulkExporter underneath it). FhirBulkExporter is a stateful,
    // one-instance-one-export-session service, so sharing a single IExportRunner instance across every
    // task in a tick - as constructor injection into TaskScheduler would do - previously made every
    // task after the first fail with "session already completed". ScopeTrackingExportRunner records
    // its own instance id per call so the two calls below can be asserted as genuinely distinct.
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
