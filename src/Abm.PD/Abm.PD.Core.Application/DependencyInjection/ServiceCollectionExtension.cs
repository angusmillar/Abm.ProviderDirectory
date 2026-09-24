using Abm.Core.HostedService;
using Abm.PD.Core.Application.FhirTaskDispatcher;
using Abm.PD.Core.Application.Identifers;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.SeedProviderDirectoryTask;
using Abm.PD.Core.Application.Settings;
using FhirNavigator;
using FhirNavigator.FhirHttpClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Application.DependencyInjection;

public static class ServiceCollectionExtension
{
    /// <summary>
    /// Registers FhirNavigator's IFhirHttpClientFactory and IHttpClientFactory, keyed by each configured
    /// repository's Code. Kept separate from <see cref="AddCoreProviderDirectoryServices"/> so a caller that
    /// needs FhirNavigator but not the TaskScheduler/ExportTaskRunner/SeedDirectoryTaskRunner machinery — Abm.PD.Console
    /// today — can call just this one.
    ///
    /// Deliberately duplicated in Abm.PD.BulkExport.DependencyInjection.ServiceCollectionExtension, which is
    /// intended to be portable to a different solution later and so cannot depend on this project for its own
    /// FhirNavigator wiring. FhirNavigator.AddFhirNavigator no-ops if it has already run, so it is safe for both
    /// copies — or a caller using both projects — to call this in any order.
    /// </summary>
    public static IServiceCollection AddFhirNavigatorServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        FhirNavigatorSettings? fhirNavigatorSettings = configuration.GetRequiredSection(
                key: FhirNavigatorSettings.SectionName)
            .Get<FhirNavigatorSettings>();

        ArgumentNullException.ThrowIfNull(fhirNavigatorSettings);

        services.AddFhirNavigator(settings =>
        {
            settings.UserAgentName = fhirNavigatorSettings.UserAgentName;
            settings.UserAgentVersion = fhirNavigatorSettings.UserAgentVersion;
            settings.FhirRepositories = fhirNavigatorSettings.FhirRepositories;
            settings.Proxy = fhirNavigatorSettings.Proxy;
        });

        return services;
    }

    public static IServiceCollection AddCoreProviderDirectoryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Skipped when an IFhirHttpClientFactory is already registered - see the equivalent guard on
        // Abm.PD.BulkExport.DependencyInjection.ServiceCollectionExtension.AddFhirBulkExportServices.
        if (services.All(descriptor => descriptor.ServiceType != typeof(IFhirHttpClientFactory)))
        {
            services.AddFhirNavigatorServices(configuration);
        }

        services.AddOptions<TaskSchedulerSettings>()
            .Bind(configuration.GetSection(TaskSchedulerSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SourceResourceLoaderSettings>()
            .Bind(configuration.GetSection(SourceResourceLoaderSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ProviderDirectorySettings>()
            .Bind(configuration.GetSection(ProviderDirectorySettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        services.AddScoped<IFhirTaskHandlerFactory, FhirTaskHandlerFactory>();
        
        // AddTimedHostedService<T>'s configurator runs synchronously at registration time, before the
        // host is built, so it cannot resolve IOptions<T> from the container the way the settings
        // above are read once the app starts - PollInterval is read straight off configuration here
        // instead, landing on the same bound value either way.
        TaskSchedulerSettings schedulerSettings = configuration
            .GetSection(TaskSchedulerSettings.SectionName)
            .Get<TaskSchedulerSettings>() ?? new TaskSchedulerSettings();

        services.AddScoped<ISourceResourceLoader, SourceResourceLoader>();
        services.AddSingleton<IdentifierSystemSupport>();
        
        services.AddKeyedScoped<ITaskHandler, SeedProviderDirectoryTaskHandler>(FhirTaskHandlerType.SeedProviderDirectory);
        
        services.AddTimedHostedService<FhirTaskDispatcher.FhirTaskDispatcher>(opt =>
        {
            opt.TriggersEvery = schedulerSettings.PollInterval;
        });

        return services;
    }
}
