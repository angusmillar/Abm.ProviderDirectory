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

    private static ExportLoaderTask NewTask(int id, string code)
    {
        return new ExportLoaderTask
        {
            Id = id,
            Code = code,
            DisplayName = $"Task {code}",
            Description = null,
            State = TaskStateId.Ready,
            StateReason = null,
            TriggerEvery = TimeSpan.FromHours(24),
            ToStartAtUtc = null,
            ToEndAtUtc = null,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            LastStart = null,
            LastEnd = null,
            DataSourceId = 1,
            DataSource = new DataSource { Id = 1, Code = "test-data-source", DisplayName = "Test Data Source" },
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    // Regression test for the finding that ExportLoaderTaskScheduler used to constructor-inject
    // IExportRunner directly, so one instance served every due task in a tick. IExportRunner (and
    // the scoped IFhirExporter/IFhirBulkExporter underneath it) is Scoped and stateful - a single
    // instance's second call to Run would hit FhirBulkExporter's "session already completed" guard.
    // The fix gives each task its own DI scope; this test proves two due tasks in one DoWork call
    // are each served by a different IExportRunner instance.
    [Fact]
    public async Task DoWork_TwoDueTasks_EachGetsItsOwnExportRunnerInstance()
    {
        List<(int TaskId, Guid InstanceId)> calls = [];
        List<ExportLoaderTask> seededTasks = [NewTask(1, "task-one"), NewTask(2, "task-two")];

        ServiceCollection services = new();
        services.AddSingleton(calls);
        services.AddScoped<IExportRunner, ScopeTrackingExportRunner>();
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
}
