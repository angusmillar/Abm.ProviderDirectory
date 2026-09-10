using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Abm.PD.Core.Api.Tests.Fixtures;

public class IntegrationTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder().Build();

    private Respawner _respawner = default!;
    private CoreApiWebApplicationFactory _factory = default!;
    private string _connectionString = default!;

    public HttpClient HttpClient { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        // 1. Start the Postgres container.
        await _postgreSqlContainer.StartAsync();
        _connectionString = _postgreSqlContainer.GetConnectionString();

        // 2. Apply EF Core migrations against it, reusing the same Npgsql/snake-case/retry
        //    configuration as every other call site (see NpgsqlDbContextOptionsSupport's own
        //    doc comment) so this fixture cannot silently drift from production configuration.
        DbContextOptionsBuilder<ProviderDirectoryDbContext> optionsBuilder = new();
        NpgsqlDbContextOptionsSupport.ConfigureProviderDirectoryDbContext(
            optionsBuilder: optionsBuilder,
            connectionString: _connectionString,
            enableRetryOnFailure: false);
        await using (ProviderDirectoryDbContext context = new(optionsBuilder.Options))
        {
            await context.Database.MigrateAsync();
        }

        // 3. Checkpoint the migrated, empty database - only the resource table exists today; add
        //    more tables here as the schema grows.
        await using (NpgsqlConnection checkpointConnection = new(_connectionString))
        {
            await checkpointConnection.OpenAsync();
            _respawner = await Respawner.CreateAsync(checkpointConnection, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToInclude = [new Respawn.Graph.Table("resource")],
            });
        }

        // 4. Start the API in-process against the container.
        _factory = new CoreApiWebApplicationFactory(_connectionString);
        HttpClient = _factory.CreateClient();
    }

    public async Task ResetDatabaseAsync()
    {
        await using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        HttpClient.Dispose();
        await _factory.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
    }
}
