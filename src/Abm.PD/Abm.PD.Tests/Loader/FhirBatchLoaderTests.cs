using System.Net;
using Abm.PD.Domain.Exceptions;
using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Domain.Loader;
using Abm.PD.Tests.TestDoubles;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Tests.Loader;

/// <summary>
/// Covers the load half: turning the export's streamed resources into batch Bundles of PUT entries and reading
/// the per entry outcomes back out of the target server's batch-response.
/// </summary>
public class FhirBatchLoaderTests
{
    [Fact]
    public async Task Load_CommitsOneBatchEachTimeTheThresholdIsReached()
    {
        using FhirBatchLoaderHarness harness = new(batchSize: 2);
        harness.RespondToEveryCommitWithSuccess();
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 4);

        FhirBatchLoadResult result = await harness.Loader.Load(source.ReadAsync(), CancellationToken.None);

        Assert.Equal(2, result.BatchCount);
        Assert.Equal(4, result.CommittedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.All(harness.CommittedBundles(), bundle => Assert.Equal(2, bundle.Entry.Count));
    }

    [Fact]
    public async Task Load_CommitsTheFinalPartialBatch()
    {
        using FhirBatchLoaderHarness harness = new(batchSize: 2);
        harness.RespondToEveryCommitWithSuccess();
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 5);

        FhirBatchLoadResult result = await harness.Loader.Load(source.ReadAsync(), CancellationToken.None);

        Assert.Equal(3, result.BatchCount);
        Assert.Equal(5, result.CommittedCount);

        //An export does not divide evenly into batches, so the remainder has to be committed on its own.
        Assert.Equal([2, 2, 1], harness.CommittedBundles().Select(bundle => bundle.Entry.Count));
    }

    [Fact]
    public async Task Load_AnEmptyExportCommitsNothing()
    {
        using FhirBatchLoaderHarness harness = new(batchSize: 2);
        harness.RespondToEveryCommitWithSuccess();
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 0);

        FhirBatchLoadResult result = await harness.Loader.Load(source.ReadAsync(), CancellationToken.None);

        Assert.Equal(0, result.BatchCount);
        Assert.Empty(harness.Handler.ReceivedRequests);
    }

    [Fact]
    public async Task Load_EachEntryIsAPutAddressingTheResourceByTypeAndId()
    {
        using FhirBatchLoaderHarness harness = new(batchSize: 2);
        harness.RespondToEveryCommitWithSuccess();
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 2);

        await harness.Loader.Load(source.ReadAsync(), CancellationToken.None);

        Bundle committed = Assert.Single(harness.CommittedBundles());
        Assert.Equal(Bundle.BundleType.Batch, committed.Type);
        Assert.Equal(
            ["Practitioner/1", "Practitioner/2"],
            committed.Entry.Select(entry => entry.Request.Url));
        Assert.All(committed.Entry, entry => Assert.Equal(Bundle.HTTPVerb.PUT, entry.Request.Method));
    }

    [Fact]
    public async Task Load_NoEntryCarriesAnIfMatch()
    {
        using FhirBatchLoaderHarness harness = new(batchSize: 2);
        harness.RespondToEveryCommitWithSuccess();
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 2);

        await harness.Loader.Load(source.ReadAsync(), CancellationToken.None);

        //A version aware update would be refused with a 409 against a stub the target created earlier for a
        //reference that had not been loaded yet, and filling those stubs in is the whole point of the PUT.
        Bundle committed = Assert.Single(harness.CommittedBundles());
        Assert.All(committed.Entry, entry => Assert.Null(entry.Request.IfMatch));
    }

    [Fact]
    public async Task Load_ReportsTheEntriesTheTargetServerRefused()
    {
        using FhirBatchLoaderHarness harness = new(batchSize: 2);
        harness.RespondToEveryCommit(() => HttpResponses.BatchResponse(
            ("200 OK", null),
            ("422 Unprocessable Entity", "Practitioner.name is not valid")));

        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 2);

        FhirBatchLoadResult result = await harness.Loader.Load(source.ReadAsync(), CancellationToken.None);

        //The batch itself answered 200 OK, so the only report of the refused resource is its own entry.
        Assert.Equal(1, result.CommittedCount);
        Assert.Equal(1, result.FailedCount);

        FhirBatchLoadFailure failure = Assert.Single(result.RetainedFailures);
        Assert.Equal("Practitioner", failure.ResourceType);
        Assert.Equal("2", failure.ResourceId);
        Assert.Equal(422, failure.HttpStatusCode);
        Assert.Equal(2, failure.LineNumber);
        Assert.Contains(failure.ErrorMessages, message => message.Contains("Practitioner.name is not valid"));
    }

    [Fact]
    public async Task Load_AResourceWithNoIdIsReportedAndNeverSent()
    {
        using FhirBatchLoaderHarness harness = new(batchSize: 2);
        harness.RespondToEveryCommitWithSuccess();

        RecordingExportResourceSource source = new(
        [
            RecordingExportResourceSource.ExportResource(new Practitioner { Id = "1" }, lineNumber: 1),
            RecordingExportResourceSource.ExportResource(new Practitioner { Id = null }, lineNumber: 2),
            RecordingExportResourceSource.ExportResource(new Practitioner { Id = "3" }, lineNumber: 3)
        ]);

        FhirBatchLoadResult result = await harness.Loader.Load(source.ReadAsync(), CancellationToken.None);

        //A PUT addresses the resource by its id, so the resource without one is skipped and the other two still
        //fill a batch between them.
        Assert.Equal(3, result.SubmittedCount);
        Assert.Equal(2, result.CommittedCount);
        Assert.Equal(1, result.FailedCount);

        Bundle committed = Assert.Single(harness.CommittedBundles());
        Assert.Equal(["Practitioner/1", "Practitioner/3"], committed.Entry.Select(entry => entry.Request.Url));
    }

    [Fact]
    public async Task Load_RetainsOnlyTheConfiguredNumberOfFailures()
    {
        using FhirBatchLoaderHarness harness = new(batchSize: 4, maxRetainedFailures: 2);
        harness.RespondToEveryCommit(() => HttpResponses.BatchResponseAll("422 Unprocessable Entity", 4));
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 4);

        FhirBatchLoadResult result = await harness.Loader.Load(source.ReadAsync(), CancellationToken.None);

        //Every failure is counted, but a load that fails on every resource must not grow a list in proportion to
        //the size of the export.
        Assert.Equal(4, result.FailedCount);
        Assert.Equal(2, result.RetainedFailures.Count);
    }

    [Fact]
    public async Task Load_KeepsReadingTheExportWhileABatchCommitIsInFlight()
    {
        using FhirBatchLoaderHarness harness = new(batchSize: 2);
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 6);

        int commitCount = 0;
        GatedHttpContent? firstCommit = null;
        harness.Handler.RespondTo(
            predicate: request => request.Method == HttpMethod.Post,
            respond: _ =>
            {
                if (++commitCount > 1)
                {
                    return HttpResponses.BatchResponseAll("200 OK", 2);
                }

                HttpResponseMessage gated = GatedHttpContent.Response(
                    BatchResponseBodyOfTwoOk(),
                    out GatedHttpContent gatedContent);

                firstCommit = gatedContent;
                return gated;
            });

        Task<FhirBatchLoadResult> loadTask = harness.Loader.Load(source.ReadAsync(), CancellationToken.None);

        //While the first commit is still in flight the loader must gather the next batch, so the read reaches
        //four resources: the two being committed and the two filling the batch behind them.
        await WaitUntil(() => source.PulledCount == 4, "the loader to gather a batch behind the commit in flight");

        //And it must stop there rather than reading ahead without limit, which is what keeps the memory bounded.
        await Task.Delay(50);
        Assert.Equal(4, source.PulledCount);

        Assert.NotNull(firstCommit);
        firstCommit.Release();

        FhirBatchLoadResult result = await loadTask;

        Assert.Equal(3, result.BatchCount);
        Assert.Equal(6, result.CommittedCount);
    }

    [Fact]
    public async Task Load_AFailureOfTheCommitItselfStopsTheLoad()
    {
        using FhirBatchLoaderHarness harness = new(batchSize: 2);
        harness.RespondToEveryCommit(() => HttpResponses.Empty(HttpStatusCode.InternalServerError));
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 6);

        //The retry policy has already given up by the time a commit fails, so this is systemic rather than one
        //bad resource, and the load stops instead of carrying on into the rest of the export.
        await Assert.ThrowsAsync<FhirOperationException>(
            () => harness.Loader.Load(source.ReadAsync(), CancellationToken.None));

        Assert.True(source.PulledCount < 6);
    }

    [Fact]
    public async Task Load_AResponseWithTheWrongEntryCountThrows()
    {
        using FhirBatchLoaderHarness harness = new(batchSize: 2);
        harness.RespondToEveryCommit(() => HttpResponses.BatchResponseAll("200 OK", 1));
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 2);

        //Without one response entry per request entry, in order, no outcome can be matched to the resource that
        //produced it, so the counts are not quietly reported against the wrong resources.
        FhirBulkLoadException exception = await Assert.ThrowsAsync<FhirBulkLoadException>(
            () => harness.Loader.Load(source.ReadAsync(), CancellationToken.None));

        Assert.Contains("response entries", exception.Message);
    }

    private static string BatchResponseBodyOfTwoOk()
    {
        using HttpResponseMessage response = HttpResponses.BatchResponseAll("200 OK", 2);
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static async Task WaitUntil(
        Func<bool> condition,
        string because)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for {because}.");
            }

            await Task.Delay(10);
        }
    }
}
