using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.Tests.TestDoubles;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Tests;

public class ExportRunnerTests
{
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
            ToStartAtUtc = null,
            ToEndAtUtc = null,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = nowUtc,
            LastEnd = null,
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
        ExportRunner runner = new(NullLogger<ExportRunner>.Instance, fakeExporter, fakeLoader);
        ExportTask task = NewTask();

        SourceResourceLoadResult result = await runner.Run(task, CancellationToken.None);

        Assert.Equal(1, result.CommittedCount);
        Assert.Single(fakeLoader.ReceivedResources);
        Assert.Equal(fakeExporter.JobId, fakeLoader.ReceivedJobId);
        Assert.Same(task.DataSource, fakeLoader.ReceivedDataSource);
        Assert.NotNull(fakeExporter.ReceivedParameters);
    }

    [Fact]
    public async Task Run_NullManifest_ThrowsArgumentNullException()
    {
        FakeFhirExporter fakeExporter = new() { ManifestToReturn = null };
        FakeSourceResourceLoader fakeLoader = new();
        ExportRunner runner = new(NullLogger<ExportRunner>.Instance, fakeExporter, fakeLoader);

        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.Run(NewTask(), CancellationToken.None));
    }
}
