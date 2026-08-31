using System.Net;
using Abm.PD.Domain.Exceptions;
using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Tests.TestData;
using Abm.PD.Tests.TestDoubles;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Tests.FhirBulkExport;

/// <summary>
/// Covers step 3 of the export flow: streaming every NDJSON file the Output Manifest lists.
/// </summary>
public class FhirBulkExporterGetExportTests
{
    private static async Task<List<FhirBulkExportResource>> DrainAsync(
        IAsyncEnumerable<FhirBulkExportResource> resources)
    {
        List<FhirBulkExportResource> drained = [];
        await foreach (FhirBulkExportResource resource in resources)
        {
            drained.Add(resource);
        }

        return drained;
    }

    [Fact]
    public async Task GetExport_ReadingBeforeTheExportHasBegunThrows()
    {
        using FhirBulkExporterHarness harness = new();

        FhirBulkExportException exception = await Assert.ThrowsAsync<FhirBulkExportException>(
            () => DrainAsync(harness.Exporter.GetExport(CancellationToken.None)));

        Assert.Contains(nameof(FhirBulkExportSessionStatus.NotStarted), exception.Message);
    }

    [Fact]
    public async Task GetExport_ReadingWhileTheServerIsStillBuildingThrows()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();

        FhirBulkExportException exception = await Assert.ThrowsAsync<FhirBulkExportException>(
            () => DrainAsync(harness.Exporter.GetExport(CancellationToken.None)));

        Assert.Contains(nameof(FhirBulkExportSessionStatus.InProgress), exception.Message);
    }

    [Fact]
    public async Task GetExport_YieldsEveryResourceOfTheOutputFile()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.PractitionerNdJson));

        List<FhirBulkExportResource> resources = await DrainAsync(harness.Exporter.GetExport(CancellationToken.None));

        Assert.Equal(2, resources.Count);
        Assert.Equal(["practitioner-1", "practitioner-2"], resources.Select(resource => resource.Resource.Id));
        Assert.All(resources, resource => Assert.IsType<Practitioner>(resource.Resource));
    }

    [Fact]
    public async Task GetExport_CarriesEnoughOriginToTraceAResourceBackToItsLine()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.PractitionerNdJson));

        List<FhirBulkExportResource> resources = await DrainAsync(harness.Exporter.GetExport(CancellationToken.None));

        Assert.Collection(
            resources,
            resource =>
            {
                Assert.Equal(1L, resource.LineNumber);
                Assert.Equal("Practitioner", resource.ManifestOutputType);
                Assert.Equal(new Uri(TestUrls.PractitionerOutputFileUrl), resource.SourceUrl);
            },
            resource =>
            {
                Assert.Equal(2L, resource.LineNumber);
                Assert.Equal("Practitioner", resource.ManifestOutputType);
                Assert.Equal(new Uri(TestUrls.PractitionerOutputFileUrl), resource.SourceUrl);
            });
    }

    [Fact]
    public async Task GetExport_AsksForNdJsonWhenItFetchesAnOutputFile()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.PractitionerNdJson));

        await DrainAsync(harness.Exporter.GetExport(CancellationToken.None));

        RecordedRequest request = harness.Handler.ReceivedRequests.Last();

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains(request.Headers.Accept, header => header.MediaType == "application/fhir+ndjson");
        Assert.Contains(request.Headers.AcceptEncoding, header => header.Value == "gzip");
    }

    [Fact]
    public async Task GetExport_ReadsEveryOutputFileInManifestOrder()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.TwoOutputFilesManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.PractitionerNdJson));
        harness.Handler.RespondToUrl(
            TestUrls.OrganizationOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.OrganizationNdJson));

        List<FhirBulkExportResource> resources = await DrainAsync(harness.Exporter.GetExport(CancellationToken.None));

        Assert.Equal(
            ["practitioner-1", "practitioner-2", "organization-1"],
            resources.Select(resource => resource.Resource.Id));

        //The line number restarts within each file, so it is only meaningful alongside the source URL.
        Assert.Equal(1L, resources[2].LineNumber);
        Assert.Equal("Organization", resources[2].ManifestOutputType);
    }

    [Fact]
    public async Task GetExport_DoesNotFetchTheNextOutputFileUntilTheCurrentOneIsExhausted()
    {
        //An export of any size must never hold more than one resource in memory, so the chain from GetExport
        //through to the NDJSON reader has to stay lazy end to end.
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.TwoOutputFilesManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.PractitionerNdJson));
        harness.Handler.RespondToUrl(
            TestUrls.OrganizationOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.OrganizationNdJson));

        await using IAsyncEnumerator<FhirBulkExportResource> enumerator =
            harness.Exporter.GetExport(CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("practitioner-1", enumerator.Current.Resource.Id);

        Assert.DoesNotContain(
            harness.Handler.ReceivedRequests,
            request => request.RequestUri == new Uri(TestUrls.OrganizationOutputFileUrl));
    }

    [Fact]
    public async Task GetExport_DoesNotBufferAnOutputFileBeforeYieldingItsFirstResource()
    {
        //SendAsync uses HttpCompletionOption.ResponseHeadersRead; without it the whole file lands in memory
        //before the first resource is handed back.
        string ndJson = string.Join(
            "\n",
            Enumerable.Range(1, 500).Select(number => $"{{\"resourceType\":\"Practitioner\",\"id\":\"practitioner-{number}\"}}"));

        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());

        ReadTrackingStream? trackedStream = null;
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () =>
            {
                HttpResponseMessage response = HttpResponses.NdJsonOverTrackedStream(ndJson, out ReadTrackingStream stream);
                trackedStream = stream;
                return response;
            });

        await using IAsyncEnumerator<FhirBulkExportResource> enumerator =
            harness.Exporter.GetExport(CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("practitioner-1", enumerator.Current.Resource.Id);

        Assert.NotNull(trackedStream);
        Assert.False(trackedStream!.ReadToEnd);
        Assert.True(trackedStream.BytesRead < trackedStream.TotalBytes);
    }

    [Fact]
    public async Task GetExport_DecompressesAnOutputFileTheHandlerHasNotAlreadyDecompressed()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.GzippedNdJson(BulkExportTestData.PractitionerNdJson));

        List<FhirBulkExportResource> resources = await DrainAsync(harness.Exporter.GetExport(CancellationToken.None));

        Assert.Equal(["practitioner-1", "practitioner-2"], resources.Select(resource => resource.Resource.Id));
    }

    [Fact]
    public async Task GetExport_AnEmptyOutputArrayYieldsNothingAndFetchesNothing()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.EmptyOutputManifest());
        int requestsAfterPolling = harness.Handler.ReceivedRequests.Count;

        List<FhirBulkExportResource> resources = await DrainAsync(harness.Exporter.GetExport(CancellationToken.None));

        Assert.Empty(resources);
        Assert.Equal(requestsAfterPolling, harness.Handler.ReceivedRequests.Count);
    }

    [Fact]
    public async Task GetExport_AManifestWithNoOutputMemberYieldsNothing()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.NoOutputMemberManifest());

        Assert.Empty(await DrainAsync(harness.Exporter.GetExport(CancellationToken.None)));
    }

    [Theory]
    [InlineData("application/fhir+ndjson")]
    [InlineData("application/ndjson")]
    [InlineData("ndjson")]
    [InlineData("APPLICATION/FHIR+NDJSON")]
    public async Task GetExport_AcceptsAnyNdJsonOutputFormat(
        string outputFormat)
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.ManifestWithOutputFormat(outputFormat));
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.PractitionerNdJson));

        Assert.Equal(2, (await DrainAsync(harness.Exporter.GetExport(CancellationToken.None))).Count);
    }

    [Theory]
    [InlineData("application/fhir+json")]
    [InlineData("application/json")]
    [InlineData("text/csv")]
    public async Task GetExport_RejectsAnOutputFormatItCanNotRead(
        string outputFormat)
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.ManifestWithOutputFormat(outputFormat));

        FhirBulkExportException exception = await Assert.ThrowsAsync<FhirBulkExportException>(
            () => DrainAsync(harness.Exporter.GetExport(CancellationToken.None)));

        Assert.Contains(outputFormat, exception.Message);
    }

    [Fact]
    public async Task GetExport_AnAbsentOutputFormatIsTreatedAsNdJson()
    {
        //The specification defaults outputFormat to application/fhir+ndjson when it is not stated.
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.TwoOutputFilesManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.PractitionerNdJson));
        harness.Handler.RespondToUrl(
            TestUrls.OrganizationOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.OrganizationNdJson));

        Assert.Equal(3, (await DrainAsync(harness.Exporter.GetExport(CancellationToken.None))).Count);
    }

    [Fact]
    public async Task GetExport_ReadsOnlyTheOutputFilesAndNotTheOutcomeOrDeletedFiles()
    {
        //The outcome and deleted files hold OperationOutcome resources and transaction Bundles, not exported
        //resources, so GetExport must leave them alone.
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.FullBallotManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.PractitionerNdJson));

        List<FhirBulkExportResource> resources = await DrainAsync(harness.Exporter.GetExport(CancellationToken.None));

        Assert.Equal(2, resources.Count);
        Assert.DoesNotContain(
            harness.Handler.ReceivedRequests,
            request => request.RequestUri.ToString().Contains("outcome-1") ||
                       request.RequestUri.ToString().Contains("deleted-1"));
    }

    [Fact]
    public async Task GetExport_SkipsBlankLinesInAnOutputFile()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(
                "{\"resourceType\":\"Practitioner\",\"id\":\"practitioner-1\"}\n\n{\"resourceType\":\"Practitioner\",\"id\":\"practitioner-2\"}\n"));

        List<FhirBulkExportResource> resources = await DrainAsync(harness.Exporter.GetExport(CancellationToken.None));

        Assert.Equal(2, resources.Count);
        Assert.Equal(3L, resources[1].LineNumber);
    }

    [Fact]
    public async Task GetExport_AMalformedLineFailsNamingTheLineItCouldNotRead()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(
                "{\"resourceType\":\"Practitioner\",\"id\":\"practitioner-1\"}\nthis is not a resource"));

        NdJsonException exception = await Assert.ThrowsAsync<NdJsonException>(
            () => DrainAsync(harness.Exporter.GetExport(CancellationToken.None)));

        Assert.Contains("line 2", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetExport_AnOutputFileThatCanNotBeFetchedThrows(
        HttpStatusCode statusCode)
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());
        harness.Handler.RespondToUrl(TestUrls.PractitionerOutputFileUrl, () => HttpResponses.Empty(statusCode));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => DrainAsync(harness.Exporter.GetExport(CancellationToken.None)));
    }

    [Fact]
    public async Task GetExport_ObservesCancellation()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.PractitionerNdJson));

        using CancellationTokenSource cancellationTokenSource = new();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DrainAsync(harness.Exporter.GetExport(cancellationTokenSource.Token)));
    }

    [Fact]
    public async Task GetExport_CanBeEnumeratedMoreThanOnceFromTheSameCompletedManifest()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());
        harness.Handler.RespondToUrl(
            TestUrls.PractitionerOutputFileUrl,
            () => HttpResponses.NdJson(BulkExportTestData.PractitionerNdJson));

        List<FhirBulkExportResource> first = await DrainAsync(harness.Exporter.GetExport(CancellationToken.None));
        List<FhirBulkExportResource> second = await DrainAsync(harness.Exporter.GetExport(CancellationToken.None));

        Assert.Equal(
            first.Select(resource => resource.Resource.Id),
            second.Select(resource => resource.Resource.Id));
    }
}
