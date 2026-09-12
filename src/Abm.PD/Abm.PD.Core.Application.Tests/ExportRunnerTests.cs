using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Loader;
using Abm.PD.Core.Application.Tests.TestDoubles;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Tests;

public class ExportRunnerTests
{
    private static ExportLoaderTask NewTask()
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new ExportLoaderTask
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
            Parameter = new ExportParameter { Type = "Patient", Since = null, TypeFilterList = ["Patient"] },
        };
    }

    [Fact]
    public async Task Run_StreamsExportIntoBatchLoader_ReturnsLoadResult()
    {
        FakeFhirExporter fakeExporter = new();
        fakeExporter.ResourcesToStream.Add(new FhirBulkExportResource(
            Resource: new Patient { Id = "1" },
            ManifestOutputType: "Patient",
            SourceUrl: new Uri("https://export.test/Patient.ndjson"),
            LineNumber: 1));
        FakeFhirBatchLoader fakeLoader = new()
        {
            ResultToReturn = new FhirBatchLoadResult(
                SubmittedCount: 1, CommittedCount: 1, FailedCount: 0, BatchCount: 1, RetainedFailures: []),
        };
        ExportRunner runner = new(NullLogger<ExportRunner>.Instance, fakeExporter, fakeLoader);

        FhirBatchLoadResult result = await runner.Run(NewTask(), CancellationToken.None);

        Assert.Equal(1, result.CommittedCount);
        Assert.Single(fakeLoader.ReceivedResources);
        Assert.NotNull(fakeExporter.ReceivedParameters);
    }

    [Fact]
    public async Task Run_NullManifest_ThrowsArgumentNullException()
    {
        FakeFhirExporter fakeExporter = new() { ManifestToReturn = null };
        FakeFhirBatchLoader fakeLoader = new();
        ExportRunner runner = new(NullLogger<ExportRunner>.Instance, fakeExporter, fakeLoader);

        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.Run(NewTask(), CancellationToken.None));
    }
}
