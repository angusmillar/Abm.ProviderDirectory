using Abm.Core.Time;
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.HttpClientSupport;
using Abm.PD.BulkExport.Loader;
using Abm.PD.BulkExport.Settings;
using Abm.PD.BulkExport.Writer;
using FhirNavigator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Abm.PD.BulkExport.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddFhirBulkExportServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        //Load all settings
        services.AddOptions<TimeSettings>()
            .Bind(configuration.GetSection(TimeSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        services.AddOptions<FhirBatchLoaderSettings>()
            .Bind(configuration.GetSection(FhirBatchLoaderSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        services.AddOptions<FhirDiskWriterSettings>()
            .Bind(configuration.GetSection(FhirDiskWriterSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        //Set up the FhirNavigator
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
        
        //HttpClient.Timeout is end to end and covers reading the response body, not just the wait for its
        //headers, and FhirNavigator sets no timeout so both clients would otherwise carry IHttpClientFactory's
        //100-second default. On the export client that would bound how long the caller may take to consume the
        //streamed output files; on the target client it would bound a whole batch commit. Neither belongs on a
        //clock — stopping the work is the CancellationToken's job.
        string[] extendedTimeoutClientList =
        [
            HttpClientType.ProviderConnectAustralia,
            HttpClientType.TargetProviderDirectoryServer
        ];

        foreach (string httpClientName in extendedTimeoutClientList)
        {
            services.Configure<HttpClientFactoryOptions>(
                httpClientName,
                options => options.HttpClientActions.Add(
                    httpClient => httpClient.Timeout = Timeout.InfiniteTimeSpan));
        }
        
        // Add Services
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<IFhirBulkExporter, FhirBulkExporter>();
        services.AddScoped<IFhirExporter, FhirExporter>();
        services.AddScoped<IFhirBatchLoader, FhirBatchLoader>();
        services.AddScoped<IFhirDiskWriter, FhirDiskWriter>();
        
        return services;
    }
}