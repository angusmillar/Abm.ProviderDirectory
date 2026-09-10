using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Abm.PD.Core.Repository;

/// <summary>
/// Lets `dotnet ef` commands run against this project standalone, without needing
/// Abm.PD.Core.Api's host or configuration. The connection string comes from the
/// ConnectionStrings__ProviderDirectoryDb environment variable, falling back to the local Docker
/// Compose default below — a throwaway local-dev credential (see
/// C:\Temp\DockerCompose\PostgresSQL\compose.yaml), not a production secret.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProviderDirectoryDbContext>
{
    private const string FallbackDevConnectionString =
        "Host=localhost;Port=5432;Database=provider-directory;Username=admin;Password=admin";

    public ProviderDirectoryDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ProviderDirectoryDb")
            ?? FallbackDevConnectionString;

        DbContextOptionsBuilder<ProviderDirectoryDbContext> optionsBuilder = new();
        NpgsqlDbContextOptionsSupport.ConfigureProviderDirectoryDbContext(
            optionsBuilder: optionsBuilder,
            connectionString: connectionString,
            enableRetryOnFailure: false);

        return new ProviderDirectoryDbContext(optionsBuilder.Options);
    }
}
