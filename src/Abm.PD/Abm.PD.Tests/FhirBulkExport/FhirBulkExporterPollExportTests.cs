using System.Net;
using Abm.PD.Domain.Exceptions;
using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Domain.HttpClientSupport;
using Abm.PD.Tests.TestData;
using Abm.PD.Tests.TestDoubles;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Tests.FhirBulkExport;

/// <summary>
/// Covers step 2 of the export flow: GET [base]/$export-poll-status?_jobId=..., where 202 means the server is
/// still building and 200 carries the Output Manifest.
/// </summary>
public class FhirBulkExporterPollExportTests
{
    [Fact]
    public async Task PollExport_PollingBeforeAnExportHasBegunThrows()
    {
        using FhirBulkExporterHarness harness = new();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Exporter.PollExport(CancellationToken.None));
    }

    [Fact]
    public async Task PollExport_GetsThePollStatusOperationWithTheJobIdOfTheRunningExport()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(HttpMethod.Get, "$export-poll-status", () => HttpResponses.PollInProgress());

        await harness.Exporter.PollExport(CancellationToken.None);

        RecordedRequest request = harness.Handler.ReceivedRequests.Last();

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            $"{TestUrls.ServiceBaseUrlWithSlash}$export-poll-status?_jobId={TestUrls.JobId}",
            request.RequestUri.ToString());
    }

    [Fact]
    public async Task PollExport_UsesTheProviderConnectAustraliaHttpClient()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(HttpMethod.Get, "$export-poll-status", () => HttpResponses.PollInProgress());

        await harness.Exporter.PollExport(CancellationToken.None);

        Assert.Equal(
            HttpClientType.ProviderConnectAustralia,
            Assert.Single(harness.HttpClientFactory.RequestedClientNames));
    }

    [Fact]
    public async Task PollExport_AnAcceptedResponseMeansTheServerIsStillBuilding()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(HttpMethod.Get, "$export-poll-status", () => HttpResponses.PollInProgress());

        FhirBulkExportState state = await harness.Exporter.PollExport(CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.InProgress, state.SessionStatus);
        Assert.Null(state.Manifest);
        Assert.Null(state.EndTime);
        Assert.Equal(TestUrls.JobId, state.JobId);
    }

    [Fact]
    public async Task PollExport_SurfacesTheXProgressHeader()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Get,
            "$export-poll-status",
            () => HttpResponses.PollInProgress(progress: "Building manifest, 42 of 100 files"));

        FhirBulkExportState state = await harness.Exporter.PollExport(CancellationToken.None);

        Assert.Equal("Building manifest, 42 of 100 files", state.ProgressMessage);
    }

    [Fact]
    public async Task PollExport_SurfacesRetryAfterAsTheRequestedPollDelay()
    {
        //The client is expected to honour the server's requested poll interval, so it has to reach the caller.
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Get,
            "$export-poll-status",
            () => HttpResponses.PollInProgress(retryAfter: TimeSpan.FromSeconds(120)));

        FhirBulkExportState state = await harness.Exporter.PollExport(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(120), state.RequestedPollDelay);
    }

    [Fact]
    public async Task PollExport_APollWithNoProgressHeadersCarriesNoProgressAndNoDelay()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(HttpMethod.Get, "$export-poll-status", () => HttpResponses.PollInProgress());

        FhirBulkExportState state = await harness.Exporter.PollExport(CancellationToken.None);

        Assert.Null(state.ProgressMessage);
        Assert.Null(state.RequestedPollDelay);
    }

    [Fact]
    public async Task PollExport_AnOkResponseCompletesTheSessionAndKeepsTheManifest()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Get,
            "$export-poll-status",
            () => HttpResponses.PollComplete(BulkExportTestData.SingleOutputFileManifest()));

        FhirBulkExportState state = await harness.Exporter.PollExport(CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.Completed, state.SessionStatus);
        Assert.NotNull(state.Manifest);
        Assert.Equal(TestUrls.PractitionerOutputFileUrl, Assert.Single(state.Manifest!.Output!).Url);
    }

    [Fact]
    public async Task PollExport_CompletionStampsTheEndTimeAndClearsTheInProgressDetail()
    {
        DateTimeOffset start = new(2026, 8, 31, 9, 0, 0, TimeSpan.FromHours(10));
        using FhirBulkExporterHarness harness = new(start);
        await harness.ArriveAtInProgress();

        bool completed = false;
        harness.Handler.RespondTo(
            HttpMethod.Get,
            "$export-poll-status",
            () =>
            {
                if (!completed)
                {
                    completed = true;
                    return HttpResponses.PollInProgress(progress: "still going", retryAfter: TimeSpan.FromSeconds(30));
                }

                return HttpResponses.PollComplete(BulkExportTestData.SingleOutputFileManifest());
            });

        FhirBulkExportState inProgress = await harness.Exporter.PollExport(CancellationToken.None);
        Assert.Equal("still going", inProgress.ProgressMessage);

        harness.DateTimeProvider.Advance(TimeSpan.FromMinutes(4));
        FhirBulkExportState state = await harness.Exporter.PollExport(CancellationToken.None);

        Assert.Equal(start, state.StartTime);
        Assert.Equal(start.AddMinutes(4), state.EndTime);
        Assert.Null(state.ProgressMessage);
        Assert.Null(state.RequestedPollDelay);
    }

    [Fact]
    public async Task PollExport_CanBeCalledRepeatedlyWhileTheServerIsStillBuilding()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(HttpMethod.Get, "$export-poll-status", () => HttpResponses.PollInProgress());

        await harness.Exporter.PollExport(CancellationToken.None);
        await harness.Exporter.PollExport(CancellationToken.None);
        FhirBulkExportState state = await harness.Exporter.PollExport(CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.InProgress, state.SessionStatus);
        Assert.Equal(4, harness.Handler.ReceivedRequests.Count);
    }

    [Fact]
    public async Task PollExport_AManifestThatIsNotJsonThrowsCarryingTheJsonFailure()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Get,
            "$export-poll-status",
            () => HttpResponses.PollComplete("<html>a gateway error page</html>"));

        FhirBulkExportException exception = await Assert.ThrowsAsync<FhirBulkExportException>(
            () => harness.Exporter.PollExport(CancellationToken.None));

        Assert.Contains("Output Manifest", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task PollExport_AnEmptyManifestBodyThrows()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(HttpMethod.Get, "$export-poll-status", () => HttpResponses.PollComplete("null"));

        FhirBulkExportException exception = await Assert.ThrowsAsync<FhirBulkExportException>(
            () => harness.Exporter.PollExport(CancellationToken.None));

        Assert.Contains("empty Output Manifest", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task PollExport_AnUnsuccessfulStatusThrows(
        HttpStatusCode statusCode)
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(HttpMethod.Get, "$export-poll-status", () => HttpResponses.Empty(statusCode));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => harness.Exporter.PollExport(CancellationToken.None));
    }

    [Fact]
    public async Task PollExport_AnHttpClientWithNoBaseAddressThrowsAnExplicitConfigurationError()
    {
        using FhirBulkExporterHarness harness = new(serviceBaseUrl: null);
        harness.Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.KickOffAccepted());
        await harness.Exporter.BeginExport(new Parameters(), CancellationToken.None);

        FhirBulkExportException exception = await Assert.ThrowsAsync<FhirBulkExportException>(
            () => harness.Exporter.PollExport(CancellationToken.None));

        Assert.Contains("BaseAddress", exception.Message);
        Assert.Contains(HttpClientType.ProviderConnectAustralia, exception.Message);
    }

    [Fact]
    public async Task PollExport_ABaseAddressWithoutATrailingSlashStillKeepsItsPath()
    {
        //Uri composition drops the last path segment when the base address has no trailing slash, which would
        //silently move the operation up a level, so the exporter appends one.
        using FhirBulkExporterHarness harness = new(serviceBaseUrl: "https://provider-directory.invalid.test/adha/api/v1/fhir");
        harness.Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.KickOffAccepted());
        harness.Handler.RespondTo(HttpMethod.Get, "$export-poll-status", () => HttpResponses.PollInProgress());

        await harness.Exporter.BeginExport(new Parameters(), CancellationToken.None);
        await harness.Exporter.PollExport(CancellationToken.None);

        Assert.Equal(
            $"https://provider-directory.invalid.test/adha/api/v1/fhir/$export-poll-status?_jobId={TestUrls.JobId}",
            harness.Handler.ReceivedRequests.Last().RequestUri.ToString());
    }

    [Fact]
    public async Task PollExport_EscapesTheJobIdIntoTheQueryString()
    {
        using FhirBulkExporterHarness harness = new();
        harness.Handler.RespondTo(
            HttpMethod.Post,
            "$export",
            () => HttpResponses.KickOffAccepted(
                $"{TestUrls.ServiceBaseUrlWithSlash}$export-poll-status?_jobId=job%20one%2Btwo"));
        harness.Handler.RespondTo(HttpMethod.Get, "$export-poll-status", () => HttpResponses.PollInProgress());

        await harness.Exporter.BeginExport(new Parameters(), CancellationToken.None);
        await harness.Exporter.PollExport(CancellationToken.None);

        //The job id decoded out of the Location header is "job one+two", so it has to be re-escaped on the way
        //back out or the "+" would reach the server as a space. AbsoluteUri is the escaped form actually sent;
        //Uri.ToString gives the unescaped display form.
        Assert.Equal(
            $"{TestUrls.ServiceBaseUrlWithSlash}$export-poll-status?_jobId=job%20one%2Btwo",
            harness.Handler.ReceivedRequests.Last().RequestUri.AbsoluteUri);
    }
}
