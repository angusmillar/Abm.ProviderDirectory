using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.Tasks;

public class TaskLookupSeedTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    [Fact]
    public async Task TaskStateTable_AfterMigration_ContainsOneRowPerEnumMember()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        ProviderDirectoryDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();

        List<TaskState> rows = await dbContext.TaskStates.AsNoTracking().ToListAsync();

        Assert.Equal(Enum.GetValues<TaskStateId>().Length, rows.Count);
        foreach (TaskStateId taskStateId in Enum.GetValues<TaskStateId>())
        {
            Assert.Contains(rows, x => x.TaskStateId == taskStateId && x.Name == taskStateId.ToString());
        }
    }

    [Fact]
    public async Task TaskTypeTable_AfterMigration_ContainsOneRowPerEnumMember()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        ProviderDirectoryDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();

        List<TaskType> rows = await dbContext.TaskTypes.AsNoTracking().ToListAsync();

        Assert.Equal(Enum.GetValues<TaskTypeId>().Length, rows.Count);
        foreach (TaskTypeId taskTypeId in Enum.GetValues<TaskTypeId>())
        {
            Assert.Contains(rows, x => x.TaskTypeId == taskTypeId && x.Name == taskTypeId.ToString());
        }
    }

    [Fact]
    public async Task TaskStateTable_AfterDatabaseReset_SeedDataStillPresent()
    {
        // ResetDatabaseAsync runs before every test via IntegrationTestBase.InitializeAsync - this
        // proves the Respawner ignore-list change (see IntegrationTestFixture) actually protects the
        // seeded lookup rows rather than wiping them.
        using IServiceScope scope = Fixture.Services.CreateScope();
        ProviderDirectoryDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();

        int count = await dbContext.TaskStates.AsNoTracking().CountAsync();

        Assert.Equal(Enum.GetValues<TaskStateId>().Length, count);
    }
}
