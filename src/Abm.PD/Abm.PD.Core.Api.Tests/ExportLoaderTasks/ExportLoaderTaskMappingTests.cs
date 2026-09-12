using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportLoaderTaskMappingTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    [Fact]
    public async Task SaveAndReload_ExportLoaderTask_RoundTripsOwnedParameterAndTypeFilterListArray()
    {
        DateTime nowUtc = DateTime.UtcNow;
        ExportLoaderTask task = new()
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
            writeContext.ExportLoaderTasks.Add(task);
            await writeContext.SaveChangesAsync();
        }

        // A second, independent scope/DbContext forces a real read from Postgres rather than the
        // first-level change tracker cache.
        using IServiceScope readScope = Fixture.Services.CreateScope();
        ProviderDirectoryDbContext readContext =
            readScope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
        ExportLoaderTask reloaded = await readContext.ExportLoaderTasks
            .AsNoTracking()
            .SingleAsync(x => x.Code == "bulk-import-au");

        Assert.Equal(task.DisplayName, reloaded.DisplayName);
        Assert.Equal(TaskStateId.Ready, reloaded.State);
        Assert.Equal(TaskTypeId.BulkImport, reloaded.TypeId);
        Assert.Equal(new[] { "Patient", "Practitioner" }, reloaded.Parameter.TypeFilterList);
    }
}
