using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Abm.PD.Core.Api.Tests.SourceResources;

/// <summary>
/// Covers the two things specific to source_resource's schema that no in-memory fake exercises: the jsonb
/// column actually round trips the resource payload, and (CorrelationId, ResourceType, ResourceId) is enforced
/// as a real database constraint rather than only an application-level assumption.
/// </summary>
public class SourceResourceRepositoryTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly Guid DefaultCorrelationId = Guid.Parse("018f6e6e-0000-7000-8000-000000000002");

    private readonly IntegrationTestFixture Fixture = fixture;

    private async Task<DataSource> NewPersistedDataSourceAsync(IServiceProvider services)
    {
        IDataSourceRepository dataSourceRepository = services.GetRequiredService<IDataSourceRepository>();
        return await dataSourceRepository.AddAsync(
            new DataSource { Code = Guid.NewGuid().ToString(), DisplayName = "Test Data Source" },
            CancellationToken.None);
    }

    private static SourceResource NewSourceResource(
        DataSource dataSource,
        Guid? correlationId = null,
        string resourceType = "Practitioner",
        string resourceId = "1",
        string json = "{\"resourceType\":\"Practitioner\",\"id\":\"1\"}")
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new SourceResource
        {
            CorrelationId = correlationId ?? DefaultCorrelationId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ResourceLastUpdated = DateTimeOffset.UtcNow,
            DataSourceId = dataSource.Id,
            DataSource = dataSource,
            Resource = json,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
        };
    }

    [Fact]
    public async Task AddRangeAsync_PersistsResourceJson_ReadsBackUnchanged()
    {
        using IServiceScope writeScope = Fixture.Services.CreateScope();
        DataSource dataSource = await NewPersistedDataSourceAsync(writeScope.ServiceProvider);
        ISourceResourceRepository repository = writeScope.ServiceProvider.GetRequiredService<ISourceResourceRepository>();
        string json = "{\"resourceType\":\"Practitioner\",\"id\":\"1\",\"active\":true}";

        await repository.AddRangeAsync(
            [NewSourceResource(dataSource, json: json)], CancellationToken.None);

        using IServiceScope readScope = Fixture.Services.CreateScope();
        ProviderDirectoryDbContext dbContext = readScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
        SourceResource persisted = await dbContext.SourceResources.AsNoTracking().SingleAsync(CancellationToken.None);

        // jsonb round trips the payload's content, but (as documented on SourceResourceConfiguration)
        // does not preserve object key order, so the fields are compared individually rather than by
        // raw string equality.
        using System.Text.Json.JsonDocument persistedJson = System.Text.Json.JsonDocument.Parse(persisted.Resource);
        Assert.Equal("Practitioner", persistedJson.RootElement.GetProperty("resourceType").GetString());
        Assert.Equal("1", persistedJson.RootElement.GetProperty("id").GetString());
        Assert.True(persistedJson.RootElement.GetProperty("active").GetBoolean());
        Assert.Equal(DefaultCorrelationId, persisted.CorrelationId);
        Assert.Equal("Practitioner", persisted.ResourceType);
        Assert.Equal("1", persisted.ResourceId);
        Assert.Equal(dataSource.Id, persisted.DataSourceId);
    }

    [Fact]
    public async Task AddRangeAsync_DuplicateCorrelationIdResourceTypeResourceId_ViolatesUniqueIndex()
    {
        using IServiceScope setupScope = Fixture.Services.CreateScope();
        DataSource dataSource = await NewPersistedDataSourceAsync(setupScope.ServiceProvider);

        using (IServiceScope firstScope = Fixture.Services.CreateScope())
        {
            ISourceResourceRepository repository = firstScope.ServiceProvider.GetRequiredService<ISourceResourceRepository>();
            await repository.AddRangeAsync([NewSourceResource(dataSource)], CancellationToken.None);
        }

        using IServiceScope secondScope = Fixture.Services.CreateScope();
        ISourceResourceRepository duplicateRepository = secondScope.ServiceProvider.GetRequiredService<ISourceResourceRepository>();

        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => duplicateRepository.AddRangeAsync([NewSourceResource(dataSource)], CancellationToken.None));

        PostgresException postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", postgresException.SqlState);
    }
}
