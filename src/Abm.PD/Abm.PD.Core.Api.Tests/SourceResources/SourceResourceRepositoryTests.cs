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
            SourceResourceId = resourceId,
            TargetResourceId = Guid.CreateVersion7(),
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
        Assert.Equal("1", persisted.SourceResourceId);
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

    [Fact]
    public async Task GetByCorrelationIdAsync_YieldsOnlyMatchingCorrelationIdAndResourceType_AndAllowsSaveAsIterated()
    {
        using IServiceScope setupScope = Fixture.Services.CreateScope();
        DataSource dataSource = await NewPersistedDataSourceAsync(setupScope.ServiceProvider);
        ISourceResourceRepository setupRepository = setupScope.ServiceProvider.GetRequiredService<ISourceResourceRepository>();

        Guid correlationId = Guid.NewGuid();
        await setupRepository.AddRangeAsync(
            [
                NewSourceResource(dataSource, correlationId: correlationId, resourceType: "Practitioner", resourceId: "1"),
                NewSourceResource(dataSource, correlationId: correlationId, resourceType: "Practitioner", resourceId: "2"),
                NewSourceResource(dataSource, correlationId: correlationId, resourceType: "Endpoint", resourceId: "1"),
                NewSourceResource(dataSource, correlationId: Guid.NewGuid(), resourceType: "Practitioner", resourceId: "1"),
            ],
            CancellationToken.None);

        // Repository and dbContext resolved from the same scope, matching how a real consumer would call
        // SaveChangesAsync per item - AddDbContext is scoped, so this is the one instance the repository
        // itself is using internally.
        using IServiceScope iterateScope = Fixture.Services.CreateScope();
        ISourceResourceRepository repository = iterateScope.ServiceProvider.GetRequiredService<ISourceResourceRepository>();
        ProviderDirectoryDbContext dbContext = iterateScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();

        List<string> visitedResourceIds = [];
        await foreach (SourceResource resource in repository.GetByCorrelationIdAsync(correlationId, "Practitioner", CancellationToken.None))
        {
            visitedResourceIds.Add(resource.SourceResourceId);
            resource.UpdatedUtc = resource.UpdatedUtc.AddDays(1);

            // Proves the caller can interleave a write with the still-open enumeration on the same
            // dbContext - the method must not hold a live reader across yields.
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal(["1", "2"], visitedResourceIds.OrderBy(x => x));

        using IServiceScope verifyScope = Fixture.Services.CreateScope();
        ProviderDirectoryDbContext verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
        List<SourceResource> persisted = await verifyDbContext.SourceResources
            .AsNoTracking()
            .Where(x => x.CorrelationId == correlationId)
            .ToListAsync(CancellationToken.None);

        Assert.All(
            persisted.Where(x => x.ResourceType == "Practitioner"),
            x => Assert.True(x.UpdatedUtc > x.CreatedUtc));
        Assert.All(
            persisted.Where(x => x.ResourceType == "Endpoint"),
            x => Assert.Equal(x.CreatedUtc, x.UpdatedUtc));
    }
}
