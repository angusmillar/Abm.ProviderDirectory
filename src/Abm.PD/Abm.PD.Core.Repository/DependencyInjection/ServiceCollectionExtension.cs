using Abm.PD.Core.Domain.Repositories;
using Abm.PD.Core.Repository.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Repository.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddCoreRepositoryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Resolved inside the AddDbContext delegate, not eagerly here: this delegate runs when the
        // DbContext is first built from the service provider, which is after the host has finished
        // building - so it picks up configuration overrides a WebApplicationFactory applies during
        // Build() (see Abm.PD.Core.Api.Tests's CoreApiWebApplicationFactory), where an eager read
        // here would have already captured the pre-override connection string.
        services.AddDbContext<ProviderDirectoryDbContext>(options =>
        {
            string? connectionString = configuration.GetConnectionString("ProviderDirectoryDb");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Missing required connection string 'ConnectionStrings:ProviderDirectoryDb'.");
            }

            NpgsqlDbContextOptionsSupport.ConfigureProviderDirectoryDbContext(
                optionsBuilder: options,
                connectionString: connectionString,
                enableRetryOnFailure: true);
        });

        services.AddScoped<IResourceRepository, ResourceRepository>();
        services.AddScoped<IProviderDataSourceRepository, ProviderDataSourceRepository>();
        services.AddScoped<IExportLoaderTaskRepository, ExportLoaderTaskRepository>();

        return services;
    }
}
