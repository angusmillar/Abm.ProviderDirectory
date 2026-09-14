using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportTaskMappingTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    [Fact]
    public async Task SaveAndReload_ExportTask_RoundTripsOwnedParameterAndTypeFilterListArray()
    {
        DateTime nowUtc = DateTime.UtcNow;
        DataSource dataSource = new()
        {
            Code = Guid.NewGuid().ToString(),
            DisplayName = "Provider Connect Australia",
        };

        using (IServiceScope dataSourceScope = Fixture.Services.CreateScope())
        {
            ProviderDirectoryDbContext dataSourceContext =
                dataSourceScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
            dataSourceContext.DataSource.Add(dataSource);
            await dataSourceContext.SaveChangesAsync();
        }

        ExportTask task = new()
        {
            Code = "bulk-import-au",
            DisplayName = "Bulk Import AU",
            Description = "Nightly bulk import",
            State = TaskStateId.Ready,
            StateReason = null,
            TriggerEvery = TimeSpan.FromHours(24),
            ToStartAtUtc = null,
            ToEndAtUtc = null,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = null,
            LastEnd = null,
            DataSourceId = dataSource.Id,
            DataSource = dataSource,
            Parameter = new ExportParameter
            {
                Type = "Patient,Practitioner",
                Since = DateTimeOffset.UtcNow,
                TypeFilterList = ["Patient", "Practitioner"],
            },
        };

        using (IServiceScope writeScope = Fixture.Services.CreateScope())
        {
            ProviderDirectoryDbContext writeContext =
                writeScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
            // dataSource was saved and fetched through a different DbContext above, so this context's
            // change tracker doesn't know about it yet - attach it first so EF recognises it as
            // already-existing (its key is non-default) rather than re-inserting it as new.
            writeContext.Attach(dataSource);
            writeContext.ExportTasks.Add(task);
            await writeContext.SaveChangesAsync();
        }

        // A second, independent scope/DbContext forces a real read from Postgres rather than the
        // first-level change tracker cache.
        using IServiceScope readScope = Fixture.Services.CreateScope();
        ProviderDirectoryDbContext readContext =
            readScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
        ExportTask reloaded = await readContext.ExportTasks
            .AsNoTracking()
            .SingleAsync(x => x.Code == "bulk-import-au");

        Assert.Equal(task.DisplayName, reloaded.DisplayName);
        Assert.Equal(TaskStateId.Ready, reloaded.State);
        Assert.Equal(TaskTypeId.BulkImport, reloaded.TypeId);
        Assert.Equal(new[] { "Patient", "Practitioner" }, reloaded.Parameter.TypeFilterList);
        Assert.Equal(dataSource.Id, reloaded.DataSourceId);
    }
}
