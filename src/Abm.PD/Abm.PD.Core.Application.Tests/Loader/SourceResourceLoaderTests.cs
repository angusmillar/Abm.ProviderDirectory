using Abm.Core.Time;
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.Settings;
using Abm.PD.Core.Application.Tests.Loader.TestDoubles;
using Abm.PD.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Tests.Loader;

/// <summary>
/// Covers turning the export's streamed resources into batches of SourceResource rows persisted through
/// ISourceResourceRepository - the local-store analogue of Abm.PD.BulkExport.Tests' FhirTransactionLoaderTests.
/// </summary>
public class SourceResourceLoaderTests
{
    private static readonly DataSource TestDataSource = new() { Id = 7, Code = "test-source", DisplayName = "Test Source" };
    private static readonly Guid TestCorrelationId = Guid.Parse("018f6e6e-0000-7000-8000-000000000001");

    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset Now => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset ToServiceOffset(
            DateTime utcDateTime)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc)).ToOffset(ServiceDefaultTimeZone);
        }

        public DateTimeOffset? ToServiceOffset(
            DateTime? utcDateTime)
        {
            if (utcDateTime == null)
            {
                return null;
            }
            return ToServiceOffset(utcDateTime.Value);
        }

        public TimeSpan ServiceDefaultTimeZone { get; } = TimeSpan.FromHours(10);
    }

    private static SourceResourceLoader NewLoader(
        FakeSourceResourceRepository repository,
        int batchSize = 2,
        int maxRetainedFailures = 100)
    {
        return new SourceResourceLoader(
            NullLogger<SourceResourceLoader>.Instance,
            Options.Create(new SourceResourceLoaderSettings { BatchSize = batchSize, MaxRetainedFailures = maxRetainedFailures }),
            new FixedDateTimeProvider(),
            repository);
    }

    [Fact]
    public async Task Load_CommitsOneBatchEachTimeTheThresholdIsReached()
    {
        FakeSourceResourceRepository repository = new();
        SourceResourceLoader loader = NewLoader(repository, batchSize: 2);
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 4);

        SourceResourceLoadResult result = await loader.Load(source.ReadAsync(), TestCorrelationId, TestDataSource, CancellationToken.None);

        Assert.Equal(2, result.BatchCount);
        Assert.Equal(4, result.CommittedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.All(repository.ReceivedBatches, batch => Assert.Equal(2, batch.Count));
    }

    [Fact]
    public async Task Load_CommitsTheFinalPartialBatch()
    {
        FakeSourceResourceRepository repository = new();
        SourceResourceLoader loader = NewLoader(repository, batchSize: 2);
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 5);

        SourceResourceLoadResult result = await loader.Load(source.ReadAsync(), TestCorrelationId, TestDataSource, CancellationToken.None);

        Assert.Equal(3, result.BatchCount);
        Assert.Equal(5, result.CommittedCount);
        Assert.Equal([2, 2, 1], repository.ReceivedBatches.Select(batch => batch.Count));
    }

    [Fact]
    public async Task Load_AnEmptyExportCommitsNothing()
    {
        FakeSourceResourceRepository repository = new();
        SourceResourceLoader loader = NewLoader(repository, batchSize: 2);
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 0);

        SourceResourceLoadResult result = await loader.Load(source.ReadAsync(), TestCorrelationId, TestDataSource, CancellationToken.None);

        Assert.Equal(0, result.BatchCount);
        Assert.Empty(repository.ReceivedBatches);
    }

    [Fact]
    public async Task Load_PersistsCorrelationIdResourceTypeIdAndDataSourceAlongsideTheResourceJson()
    {
        FakeSourceResourceRepository repository = new();
        SourceResourceLoader loader = NewLoader(repository, batchSize: 2);
        DateTimeOffset lastUpdated = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        RecordingExportResourceSource source = new(
        [
            RecordingExportResourceSource.ExportResource(
                RecordingExportResourceSource.NewPractitioner("1", lastUpdated), lineNumber: 1),
        ]);

        await loader.Load(source.ReadAsync(), TestCorrelationId, TestDataSource, CancellationToken.None);

        SourceResource persisted = Assert.Single(repository.ReceivedBatches.SelectMany(batch => batch));
        Assert.Equal(TestCorrelationId, persisted.CorrelationId);
        Assert.Equal("Practitioner", persisted.ResourceType);
        Assert.Equal("1", persisted.ResourceId);
        Assert.Equal(lastUpdated, persisted.ResourceLastUpdated);
        Assert.Equal(TestDataSource.Id, persisted.DataSourceId);
        Assert.Same(TestDataSource, persisted.DataSource);
        Assert.Contains("\"resourceType\":\"Practitioner\"", persisted.Resource);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), persisted.CreatedUtc);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), persisted.UpdatedUtc);
    }

    [Fact]
    public async Task Load_AResourceWithNoIdIsReportedAndNeverPersisted()
    {
        FakeSourceResourceRepository repository = new();
        SourceResourceLoader loader = NewLoader(repository, batchSize: 2);
        RecordingExportResourceSource source = new(
        [
            RecordingExportResourceSource.ExportResource(RecordingExportResourceSource.NewPractitioner("1"), lineNumber: 1),
            RecordingExportResourceSource.ExportResource(RecordingExportResourceSource.NewPractitioner(null), lineNumber: 2),
            RecordingExportResourceSource.ExportResource(RecordingExportResourceSource.NewPractitioner("3"), lineNumber: 3),
        ]);

        SourceResourceLoadResult result = await loader.Load(source.ReadAsync(), TestCorrelationId, TestDataSource, CancellationToken.None);

        Assert.Equal(3, result.SubmittedCount);
        Assert.Equal(2, result.CommittedCount);
        Assert.Equal(1, result.FailedCount);

        SourceResourceLoadFailure failure = Assert.Single(result.RetainedFailures);
        Assert.Equal("Practitioner", failure.ResourceType);
        Assert.Null(failure.ResourceId);
        Assert.Equal(2, failure.LineNumber);
    }

    [Fact]
    public async Task Load_AResourceWithNoLastUpdatedIsReportedAndNeverPersisted()
    {
        FakeSourceResourceRepository repository = new();
        SourceResourceLoader loader = NewLoader(repository, batchSize: 2);
        RecordingExportResourceSource source = new(
        [
            RecordingExportResourceSource.ExportResource(
                new Hl7.Fhir.Model.Practitioner { Id = "1", Meta = null }, lineNumber: 1),
        ]);

        SourceResourceLoadResult result = await loader.Load(source.ReadAsync(), TestCorrelationId, TestDataSource, CancellationToken.None);

        Assert.Equal(1, result.SubmittedCount);
        Assert.Equal(0, result.CommittedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Empty(repository.ReceivedBatches);
    }

    [Fact]
    public async Task Load_RetainsOnlyTheConfiguredNumberOfFailures()
    {
        FakeSourceResourceRepository repository = new();
        SourceResourceLoader loader = NewLoader(repository, batchSize: 4, maxRetainedFailures: 2);
        RecordingExportResourceSource source = new(
            Enumerable.Range(1, 4)
                .Select(_ => RecordingExportResourceSource.ExportResource(RecordingExportResourceSource.NewPractitioner(null)))
                .ToList());

        SourceResourceLoadResult result = await loader.Load(source.ReadAsync(), TestCorrelationId, TestDataSource, CancellationToken.None);

        Assert.Equal(4, result.FailedCount);
        Assert.Equal(2, result.RetainedFailures.Count);
    }

    [Fact]
    public async Task Load_KeepsReadingTheExportWhileABatchCommitIsInFlight()
    {
        FakeSourceResourceRepository repository = new();
        TaskCompletionSource firstCommitGate = new();
        int commitCount = 0;
        repository.OnAddRangeAsync = async (_, _) =>
        {
            if (Interlocked.Increment(ref commitCount) == 1)
            {
                await firstCommitGate.Task;
            }
        };

        SourceResourceLoader loader = NewLoader(repository, batchSize: 2);
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 6);

        Task<SourceResourceLoadResult> loadTask = loader.Load(source.ReadAsync(), TestCorrelationId, TestDataSource, CancellationToken.None);

        //While the first commit is still in flight the loader must gather the next batch, so the read reaches
        //four resources: the two being committed and the two filling the batch behind them.
        await WaitUntil(() => source.PulledCount == 4, "the loader to gather a batch behind the commit in flight");

        //And it must stop there rather than reading ahead without limit, which is what keeps the memory bounded.
        await Task.Delay(50);
        Assert.Equal(4, source.PulledCount);

        firstCommitGate.SetResult();

        SourceResourceLoadResult result = await loadTask;

        Assert.Equal(3, result.BatchCount);
        Assert.Equal(6, result.CommittedCount);
    }

    [Fact]
    public async Task Load_AFailureOfTheCommitItselfStopsTheLoad()
    {
        FakeSourceResourceRepository repository = new();
        repository.OnAddRangeAsync = (_, _) => throw new InvalidOperationException("Simulated database failure");
        SourceResourceLoader loader = NewLoader(repository, batchSize: 2);
        RecordingExportResourceSource source = RecordingExportResourceSource.OfPractitioners(count: 6);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.Load(source.ReadAsync(), TestCorrelationId, TestDataSource, CancellationToken.None));

        Assert.True(source.PulledCount < 6);
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
