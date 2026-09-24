using Abm.Core.Time;
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Loader;
using Abm.PD.BulkExport.Settings;
using Abm.PD.BulkExport.Writer;
using FhirNavigator;
using FhirNavigator.FhirHttpClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.BulkExport.DependencyInjection;

public static class ServiceCollectionExtension
{
    /// <summary>
    /// Registers FhirNavigator's IFhirHttpClientFactory and IHttpClientFactory, keyed by each configured
    /// repository's Code. Kept separate from <see cref="AddFhirBulkExportServices"/> so a caller that
    /// needs to use FhirNavigator in their broader project is not forced to register it twice when requiring
    /// this Abm.PD.BulkExport project.
    ///
    /// Deliberately duplicated in Abm.PD.Core.Application.DependencyInjection.ServiceCollectionExtension: this
    /// project is intended to be portable to a different solution later, so it must not depend on
    /// Abm.PD.Core.Application for its own FhirNavigator wiring.
    /// </summary>
    private static IServiceCollection AddFhirNavigatorServices(
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

    /// <summary>
    /// Registers the export/load services, plus their FhirNavigator-backed IFhirHttpClientFactory and
    /// IHttpClientFactory dependencies via <see cref="AddFhirNavigatorServices"/> — skipped when an
    /// IFhirHttpClientFactory is already registered, which covers both a caller that already called
    /// AddFhirNavigatorServices itself (Abm.PD.Core.Application's copy included; FhirNavigator.AddFhirNavigator
    /// is idempotent, but re-entering this method's own GetRequiredSection would still demand a FhirNavigator
    /// config section that caller might not have) and a test standing in its own IFhirHttpClientFactory double.
    /// </summary>
    public static IServiceCollection AddFhirBulkExportServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(IFhirHttpClientFactory)))
        {
            services.AddFhirNavigatorServices(configuration);
        }

        //Load all settings
        services.AddOptions<TimeSettings>()
            .Bind(configuration.GetSection(TimeSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<FhirBulkExporterSettings>()
            .Bind(configuration.GetSection(FhirBulkExporterSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<FhirTransactionLoaderSettings>()
            .Bind(configuration.GetSection(FhirTransactionLoaderSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<FhirDiskWriterSettings>()
            .Bind(configuration.GetSection(FhirDiskWriterSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Add Services
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<IFhirBulkExporter, FhirBulkExporter>();
        services.AddScoped<IFhirExporter, FhirExporter>();
        services.AddScoped<IFhirTransactionLoader, FhirTransactionLoader>();
        services.AddScoped<IFhirDiskWriter, FhirDiskWriter>();

        return services;
    }
}