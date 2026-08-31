using System.Net;
using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Tests.TestData;
using Abm.PD.Tests.TestDoubles;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Tests.FhirBulkExport;

/// <summary>
/// Covers step 4 of the export flow: DELETE on the poll status URL to cancel or clean up the server side job.
/// </summary>
public class FhirBulkExporterDeleteExportTests
{
    [Fact]
    public async Task DeleteExport_DeletingBeforeAnExportHasBegunThrows()
    {
        using FhirBulkExporterHarness harness = new();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Exporter.DeleteExport(CancellationToken.None));
    }

    [Fact]
    public async Task DeleteExport_DeletesThePollStatusUrlOfTheRunningJob()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Delete,
            "$export-poll-status",
            () => HttpResponses.Empty(HttpStatusCode.Accepted));

        await harness.Exporter.DeleteExport(CancellationToken.None);

        RecordedRequest request = harness.Handler.ReceivedRequests.Last();

        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(
            $"{TestUrls.ServiceBaseUrlWithSlash}$export-poll-status?_jobId={TestUrls.JobId}",
            request.RequestUri.ToString());
    }

    [Fact]
    public async Task DeleteExport_AnAcceptedResponseClearsTheWholeSession()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Delete,
            "$export-poll-status",
            () => HttpResponses.Empty(HttpStatusCode.Accepted));

        FhirBulkExportState state = await harness.Exporter.DeleteExport(CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.Deleted, state.SessionStatus);
        Assert.Null(state.JobId);
        Assert.Null(state.StartTime);
        Assert.Null(state.EndTime);
        Assert.Null(state.Manifest);
        Assert.Null(state.OperationOutcome);
        Assert.Null(state.ErrorMessages);
        Assert.Null(state.ProgressMessage);
        Assert.Null(state.RequestedPollDelay);
    }

    [Fact]
    public async Task DeleteExport_CanCancelAnExportThatHasAlreadyCompleted()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());
        harness.Handler.RespondTo(
            HttpMethod.Delete,
            "$export-poll-status",
            () => HttpResponses.Empty(HttpStatusCode.Accepted));

        FhirBulkExportState state = await harness.Exporter.DeleteExport(CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.Deleted, state.SessionStatus);
        Assert.Null(state.Manifest);
    }

    [Fact]
    public async Task DeleteExport_AnOkResponseWithAnOperationOutcomeIsAFailureCarryingTheServersReason()
    {
        //202 Accepted is the only documented success, so anything else leaves the job in an unknown state and
        //must not be reported as deleted.
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Delete,
            "$export-poll-status",
            () => HttpResponses.FhirJson(HttpStatusCode.OK, BulkExportTestData.OperationOutcomeJson));

        FhirBulkExportState state = await harness.Exporter.DeleteExport(CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.Failed, state.SessionStatus);
        Assert.NotNull(state.OperationOutcome);
        Assert.Contains("The _typeFilter is not supported", Assert.Single(state.ErrorMessages!));
    }

    [Fact]
    public async Task DeleteExport_AnUnexpectedStatusWithNoBodyStillReportsTheStatus()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Delete,
            "$export-poll-status",
            () => HttpResponses.Empty(HttpStatusCode.NoContent));

        FhirBulkExportState state = await harness.Exporter.DeleteExport(CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.Failed, state.SessionStatus);
        Assert.Null(state.OperationOutcome);
        Assert.Contains("204", Assert.Single(state.ErrorMessages!));
    }

    [Fact]
    public async Task DeleteExport_ABodyThatIsNotFhirJsonIsReportedWithoutThrowing()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Delete,
            "$export-poll-status",
            () => HttpResponses.FhirJson(HttpStatusCode.OK, "<html>a gateway error page</html>"));

        FhirBulkExportState state = await harness.Exporter.DeleteExport(CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.Failed, state.SessionStatus);
        Assert.Null(state.OperationOutcome);
        Assert.Contains("200", Assert.Single(state.ErrorMessages!));
    }

    [Fact]
    public async Task DeleteExport_ABodyHoldingSomeOtherResourceTypeIsNotMistakenForAnOutcome()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Delete,
            "$export-poll-status",
            () => HttpResponses.FhirJson(
                HttpStatusCode.OK,
                "{\"resourceType\":\"Practitioner\",\"id\":\"practitioner-1\"}"));

        FhirBulkExportState state = await harness.Exporter.DeleteExport(CancellationToken.None);

        Assert.Equal(FhirBulkExportSessionStatus.Failed, state.SessionStatus);
        Assert.Null(state.OperationOutcome);
        Assert.Contains("200", Assert.Single(state.ErrorMessages!));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task DeleteExport_AnUnsuccessfulStatusThrows(
        HttpStatusCode statusCode)
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Delete,
            "$export-poll-status",
            () => HttpResponses.Empty(statusCode));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => harness.Exporter.DeleteExport(CancellationToken.None));
    }

    [Fact]
    public async Task DeleteExport_ADeletedSessionCanNotStartANewExport()
    {
        //Deleted is not one of the statuses BeginExport accepts, so the instance is spent once the job is gone.
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtInProgress();
        harness.Handler.RespondTo(
            HttpMethod.Delete,
            "$export-poll-status",
            () => HttpResponses.Empty(HttpStatusCode.Accepted));

        await harness.Exporter.DeleteExport(CancellationToken.None);

        await Assert.ThrowsAsync<Domain.Exceptions.FhirBulkExportException>(
            () => harness.Exporter.BeginExport(new Hl7.Fhir.Model.Parameters(), CancellationToken.None));
    }
}
