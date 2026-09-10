using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Repository.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddCoreRepositoryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("ProviderDirectoryDb")
            ?? throw new InvalidOperationException(
                "Missing required connection string 'ConnectionStrings:ProviderDirectoryDb'.");

        services.AddDbContext<ProviderDirectoryDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IResourceRepository, ResourceRepository>();

        return services;
    }
}
