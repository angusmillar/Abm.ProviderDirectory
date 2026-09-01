using Abm.PD.Domain.DateTimeSupport;
using Abm.PD.Domain.Exporter;
using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Domain.HttpClientSupport;
using Abm.PD.Domain.Settings;
using FhirNavigator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Abm.PD.Domain.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddProviderDirectoryServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        services.AddOptions<ServiceDefaultTimeZoneSettings>()
            .Bind(configuration.GetSection(ServiceDefaultTimeZoneSettings.SectionName))
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
        
        //Ensure the FHIR bulk download HTTP client has an extended/infinte timeout
        services.Configure<HttpClientFactoryOptions>(
            HttpClientType.ProviderConnectAustralia,
            options => options.HttpClientActions.Add(
                httpClient => httpClient.Timeout = Timeout.InfiniteTimeSpan));
        
        // Add Services
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        
        services.AddScoped<IFhirBulkExporter, FhirBulkExporter>();
        services.AddScoped<IFhirExporter, FhirExporter>();
        
        return services;
    }
}