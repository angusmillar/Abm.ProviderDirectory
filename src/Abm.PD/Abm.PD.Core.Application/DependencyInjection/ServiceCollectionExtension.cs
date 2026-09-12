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
