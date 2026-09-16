using Abm.Core.HostedService;
using Abm.PD.Core.Application.ExportTaskRunner;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.MatchingTaskRunner;
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

        services.AddScoped<IExportTaskRunner, ExportTaskRunner.ExportTaskRunner>();
        services.AddScoped<IMatchingTaskRunner, MatchingTaskRunner.MatchingTaskRunner>();
        services.AddScoped<ISourceResourceLoader, SourceResourceLoader>();

        // AddTimedHostedService<T> already registers T (TaskScheduler) as Scoped and adds
        // the IHostedService that ticks it - no separate AddScoped<TaskScheduler>() call.
        services.AddTimedHostedService<TaskScheduler.TaskScheduler>(opt =>
        {
            opt.TriggersEvery = schedulerSettings.PollInterval;
        });

        return services;
    }
}
