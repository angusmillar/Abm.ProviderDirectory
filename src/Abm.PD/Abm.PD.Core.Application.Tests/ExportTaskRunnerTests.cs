using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.Core.Application.ExportTaskRunner;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.Settings;
using Abm.PD.Core.Application.Tests.TestDoubles;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Tests;

public class ExportTaskRunnerTests
{
    private static IOptions<ProviderDirectorySettings> NewProviderDirectorySettings()
    {
        return Options.Create(new ProviderDirectorySettings
        {
            FhirRepositoryCodeAssignment = new FhirRepositoryCodeAssignmentSettings
            {
                HealthConnectProviderDirectoryExternal = "ProviderConnectAustraliaExternal",
                HealthConnectProviderDirectoryLocal = "ProviderConnectAustraliaLocal",
                HealthLinkProviderDirectoryExternal = "HealthLinkExternal",
                HealthLinkProviderDirectoryLocal = "HealthLinkLocal",
                TelstraHealthProviderDirectoryTarget = "TelstraHealth",
            },
            FhirSystemUriSettings = new FhirSystemUriSettings()
            {
                ProviderDirectoryIgBaseUrl = new Uri("http://acme-health.fhir.com/ig"),
            }
        });
    }

    private static ExportTask NewTask()
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new ExportTask
        {
            Code = "test-task",
            DisplayName = "Test task",
            Description = null,
            State = TaskStateId.InProgress,
            StateReason = null,
            TriggerEvery = TimeSpan.FromHours(24),
            StartAtUtc = null,
            EndAtUtc = null,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStartUtc = nowUtc,
            LastEndUtc = null,
            DataSourceId = 1,
            DataSource = new DataSource { Id = 1, Code = "test-data-source", DisplayName = "Test Data Source" },
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    [Fact]
    public async Task Run_StreamsExportIntoSourceResourceLoader_ReturnsLoadResult()
    {
        FakeFhirExporter fakeExporter = new();
        fakeExporter.ResourcesToStream.Add(new FhirBulkExportResource(
            Resource: new Patient { Id = "1" },
            ManifestOutputType: "Patient",
            SourceUrl: new Uri("https://export.test/Patient.ndjson"),
            LineNumber: 1));
        FakeSourceResourceLoader fakeLoader = new()
        {
            ResultToReturn = new SourceResourceLoadResult(
                SubmittedCount: 1, CommittedCount: 1, FailedCount: 0, BatchCount: 1, RetainedFailures: []),
        };
        ExportTaskRunner.ExportTaskRunner taskRunner = new(
            NullLogger<ExportTaskRunner.ExportTaskRunner>.Instance, NewProviderDirectorySettings(), fakeExporter, fakeLoader);
        ExportTask task = NewTask();
        Guid correlationId = Guid.CreateVersion7();

        SourceResourceLoadResult result = await taskRunner.Run(task, correlationId, CancellationToken.None);

        Assert.Equal(1, result.CommittedCount);
        Assert.Single(fakeLoader.ReceivedResources);
        Assert.Equal(correlationId, fakeLoader.ReceivedCorrelationId);
        Assert.Same(task.DataSource, fakeLoader.ReceivedDataSource);
        Assert.NotNull(fakeExporter.ReceivedParameters);
        Assert.Equal("ProviderConnectAustralia", fakeExporter.ReceivedRepositoryCode);
    }

    [Fact]
    public async Task Run_NullManifest_ThrowsArgumentNullException()
    {
        FakeFhirExporter fakeExporter = new() { ManifestToReturn = null };
        FakeSourceResourceLoader fakeLoader = new();
        ExportTaskRunner.ExportTaskRunner taskRunner = new(
            NullLogger<ExportTaskRunner.ExportTaskRunner>.Instance, NewProviderDirectorySettings(), fakeExporter, fakeLoader);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => taskRunner.Run(NewTask(), Guid.CreateVersion7(), CancellationToken.None));
    }
}
