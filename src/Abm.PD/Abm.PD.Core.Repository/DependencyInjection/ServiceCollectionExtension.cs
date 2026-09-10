using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Repository.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddCoreRepositoryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("ProviderDirectoryDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Missing required connection string 'ConnectionStrings:ProviderDirectoryDb'.");
        }

        services.AddDbContext<ProviderDirectoryDbContext>(options =>
            NpgsqlDbContextOptionsSupport.ConfigureProviderDirectoryDbContext(
                optionsBuilder: options,
                connectionString: connectionString,
                enableRetryOnFailure: true));

        services.AddScoped<IResourceRepository, ResourceRepository>();

        return services;
    }
}
