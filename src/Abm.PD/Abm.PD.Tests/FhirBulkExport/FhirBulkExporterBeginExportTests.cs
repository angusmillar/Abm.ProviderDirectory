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
/// Covers step 1 of the export flow: POST [base]/$export with Prefer: respond-async, expecting 202 Accepted and
/// a Location header carrying the _jobId.
/// </summary>
public class FhirBulkExporterBeginExportTests
{
    private static Parameters ExportParameters()
    {
        Parameters parameters = new();
        parameters.Parameter.Add(new Parameters.ParameterComponent
        {
            Name = "_type",
            Value = new FhirString("Practitioner")
        });

        return parameters;
    }

    [Fact]
    public async Task BeginExport_AnAcceptedKickOffLeavesTheSessionInProgressWithTheJobId()
    {
        using FhirBulkExporterHarness harness = new();
        harness.Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.KickOffAccepted());

        FhirBulkExportState state = await harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.InProgress, state.SessionStatus);
        Assert.Equal(TestUrls.JobId, state.JobId);
        Assert.Null(state.OperationOutcome);
        Assert.Null(state.ErrorMessages);
        Assert.Null(state.Manifest);
        Assert.Null(state.EndTime);
    }

    [Fact]
    public async Task BeginExport_StampsTheStartTimeFromTheDateTimeProviderNotTheMachineClock()
    {
        DateTimeOffset now = new(2026, 8, 31, 9, 0, 0, TimeSpan.FromHours(10));
        using FhirBulkExporterHarness harness = new(now);
        harness.Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.KickOffAccepted());

        FhirBulkExportState state = await harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None);

        Assert.Equal(now, state.StartTime);
    }

    [Fact]
    public async Task BeginExport_PostsAWholeSystemExportOperationWithTheAsyncPreferHeader()
    {
        using FhirBulkExporterHarness harness = new();
        harness.Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.KickOffAccepted());

        await harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None);

        RecordedRequest request = Assert.Single(harness.Handler.ReceivedRequests);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{TestUrls.ServiceBaseUrlWithSlash}$export", request.RequestUri.ToString());
        Assert.True(request.HasHeader("Prefer", "respond-async"));
    }

    [Fact]
    public async Task BeginExport_SendsTheSuppliedParametersAsTheRequestBody()
    {
        using FhirBulkExporterHarness harness = new();
        harness.Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.KickOffAccepted());

        await harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None);

        RecordedRequest request = Assert.Single(harness.Handler.ReceivedRequests);

        Assert.NotNull(request.Body);
        Assert.Contains("\"resourceType\":\"Parameters\"", request.Body!.Replace(" ", string.Empty));
        Assert.Contains("_type", request.Body);
        Assert.Contains("Practitioner", request.Body);
    }

    [Fact]
    public async Task BeginExport_AsksForTheProviderConnectAustraliaClient()
    {
        //The repository code is the key both client factories are configured under, so the exporter must not
        //drift away from the HttpClientType constant.
        using FhirBulkExporterHarness harness = new();
        harness.Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.KickOffAccepted());

        await harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None);

        Assert.Equal(
            HttpClientType.ProviderConnectAustralia,
            Assert.Single(harness.FhirHttpClientFactory.RequestedClientNames));
    }

    [Fact]
    public async Task BeginExport_TakesTheJobIdFromAContentLocationHeaderWhenThereIsNoLocationHeader()
    {
        using FhirBulkExporterHarness harness = new();
        harness.Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.KickOffAcceptedWithContentLocation());

        FhirBulkExportState state = await harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.InProgress, state.SessionStatus);
        Assert.Equal(TestUrls.JobId, state.JobId);
    }

    [Fact]
    public async Task BeginExport_AnAcceptedResponseWithNoLocationHeaderThrows()
    {
        using FhirBulkExporterHarness harness = new();
        harness.Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.KickOffAccepted(location: null));

        FhirBulkExportException exception = await Assert.ThrowsAsync<FhirBulkExportException>(
            () => harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None));

        Assert.Contains("Content-Location", exception.Message);
    }

    [Fact]
    public async Task BeginExport_ALocationHeaderWithoutAJobIdThrows()
    {
        using FhirBulkExporterHarness harness = new();
        harness.Handler.RespondTo(
            HttpMethod.Post,
            "$export",
            () => HttpResponses.KickOffAccepted($"{TestUrls.ServiceBaseUrlWithSlash}$export-poll-status?_id=nope"));

        FhirBulkExportException exception = await Assert.ThrowsAsync<FhirBulkExportException>(
            () => harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None));

        Assert.Contains("_jobId", exception.Message);
    }

    [Fact]
    public async Task BeginExport_AnOkRatherThanAnAcceptedResponseIsAFailure()
    {
        //Only 202 means the server took the job. Anything else, however successful looking, leaves no job to
        //poll for, so the session must not be reported as InProgress.
        using FhirBulkExporterHarness harness = new();
        harness.Handler.RespondTo(
            HttpMethod.Post,
            "$export",
            () => HttpResponses.FhirJson(HttpStatusCode.OK, BulkExportTestData.OperationOutcomeJson));

        FhirBulkExportState state = await harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.Failed, state.SessionStatus);
        Assert.Null(state.JobId);
        Assert.Null(state.StartTime);
        Assert.NotNull(state.OperationOutcome);
        Assert.Contains(
            "The _typeFilter is not supported",
            Assert.Single(state.ErrorMessages!));
    }

    [Fact]
    public async Task BeginExport_AnOkResponseWithNoBodyIsAFailureThatStillReportsTheStatus()
    {
        using FhirBulkExporterHarness harness = new();
        harness.Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.Empty(HttpStatusCode.OK));

        FhirBulkExportState state = await harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.Failed, state.SessionStatus);
        Assert.Null(state.OperationOutcome);
        Assert.Contains("200", Assert.Single(state.ErrorMessages!));
    }

    [Fact]
    public async Task BeginExport_AServerErrorIsReportedAsAFailedStateRatherThanThrown()
    {
        //FHIR level failures are part of the state machine, not exceptions. Only protocol and programmer errors
        //throw out of the exporter.
        using FhirBulkExporterHarness harness = new();
        harness.Handler.RespondTo(
            HttpMethod.Post,
            "$export",
            () => HttpResponses.FhirJson(HttpStatusCode.BadRequest, BulkExportTestData.OperationOutcomeJson));

        FhirBulkExportState state = await harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.Failed, state.SessionStatus);
        Assert.NotNull(state.OperationOutcome);
        Assert.NotEmpty(state.ErrorMessages!);
    }

    [Fact]
    public async Task BeginExport_ARefusedExportCanBeRetried()
    {
        //Failed is one of the two statuses a new export may start from, so a rejected kick-off must not leave
        //the instance unusable.
        using FhirBulkExporterHarness harness = new();
        bool firstCall = true;

        harness.Handler.RespondTo(
            HttpMethod.Post,
            "$export",
            () =>
            {
                if (firstCall)
                {
                    firstCall = false;
                    return HttpResponses.FhirJson(HttpStatusCode.BadRequest, BulkExportTestData.OperationOutcomeJson);
                }

                return HttpResponses.KickOffAccepted();
            });

        FhirBulkExportState failed = await harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None);
        Assert.Equal(FhirBulkExportSessionStatus.Failed, failed.SessionStatus);

        FhirBulkExportState retried = await harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.InProgress, retried.SessionStatus);
        Assert.Equal(TestUrls.JobId, retried.JobId);
    }

    [Fact]
    public async Task BeginExport_CanNotStartASecondExportWhileOneIsInProgress()
    {
        //One exporter instance is one export session, so starting over would silently orphan the running job.
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();

        FhirBulkExportException exception = await Assert.ThrowsAsync<FhirBulkExportException>(
            () => harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None));

        Assert.Contains(nameof(FhirBulkExportSessionStatus.InProgress), exception.Message);
    }

    [Fact]
    public async Task BeginExport_CanNotStartASecondExportOnceOneHasCompleted()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());

        FhirBulkExportException exception = await Assert.ThrowsAsync<FhirBulkExportException>(
            () => harness.Exporter.BeginExport(ExportParameters(), CancellationToken.None));

        Assert.Contains(nameof(FhirBulkExportSessionStatus.Completed), exception.Message);
    }
}
