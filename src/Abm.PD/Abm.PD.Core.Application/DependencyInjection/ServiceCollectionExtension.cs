using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Application.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddCoreProviderDirectoryServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        
        services.AddScoped<IExportRunner, ExportRunner>();
        
        return services;
    }
}