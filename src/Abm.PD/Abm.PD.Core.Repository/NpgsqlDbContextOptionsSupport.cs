using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository;

// Keeps the Npgsql options (migrations history table name, snake case, retry) applied identically
// wherever a ProviderDirectoryDbContext is configured, so the two call sites cannot drift apart -
// see ServiceCollectionExtension and DesignTimeDbContextFactory.
internal static class NpgsqlDbContextOptionsSupport
{
    // EFCore.NamingConventions only rewrites model names; the internal __EFMigrationsHistory table
    // is configured via the relational options extension, so it needs renaming here explicitly.
    private const string MigrationsHistoryTableName = "__ef_migrations_history";

    // Takes the non-generic DbContextOptionsBuilder because that is what AddDbContext's
    // configuration delegate supplies; DbContextOptionsBuilder<ProviderDirectoryDbContext>
    // (used by DesignTimeDbContextFactory) derives from it, so both call sites fit here.
    internal static void ConfigureProviderDirectoryDbContext(
        DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        bool enableRetryOnFailure)
    {
        optionsBuilder
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable(MigrationsHistoryTableName);

                if (enableRetryOnFailure)
                {
                    npgsql.EnableRetryOnFailure();
                }
            })
            .UseSnakeCaseNamingConvention();
    }
}
