using Abm.PD.Domain.DateTimeSupport;
using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Domain.Settings;
using FhirNavigator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        
        // Add Services
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        
        services.AddScoped<IFhirBulkExporter, FhirBulkExporter>();
        
        
        return services;
    }
}