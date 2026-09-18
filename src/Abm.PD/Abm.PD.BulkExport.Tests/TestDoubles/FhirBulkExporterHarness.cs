using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Settings;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Abm.PD.BulkExport.Tests.TestDoubles;

/// <summary>
/// Builds a real <see cref="FhirBulkExporter"/> whose two outbound seams — the Firely FhirClient used for the
/// kick-off and the raw HttpClient used for polling, deletion and the output file downloads — both terminate in
/// the same <see cref="StubHttpMessageHandler"/>.
///
/// Everything under test is production code; only the transport is replaced.
/// </summary>
public sealed class FhirBulkExporterHarness : IDisposable
{
    public FhirBulkExporterHarness(
        DateTimeOffset? now = null,
        string? serviceBaseUrl = TestUrls.ServiceBaseUrl,
        TimeSpan? streamedExportHttpClientTimeout = null)
    {
        Handler = new StubHttpMessageHandler();

        Uri? baseAddress = serviceBaseUrl is null ? null : new Uri(serviceBaseUrl);

        FhirClient = new FhirClient(
            endpoint: new Uri(serviceBaseUrl ?? TestUrls.ServiceBaseUrl),
            settings: new FhirClientSettings
            {
                PreferredFormat = ResourceFormat.Json,
                PreferredParameterHandling = SearchParameterHandling.Lenient
            },
            messageHandler: Handler);

        DateTimeProvider = new StubDateTimeProvider(now ?? new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.FromHours(10)), TimeSpan.FromHours(10));
        FhirHttpClientFactory = new StubFhirHttpClientFactory(FhirClient);
        HttpClientFactory = new StubHttpClientFactory(Handler, baseAddress);

        Exporter = new FhirBulkExporter(
            logger: NullLogger<FhirBulkExporter>.Instance,
            dateTimeProvider: DateTimeProvider,
            fhirHttpClientFactory: FhirHttpClientFactory,
            httpClientFactory: HttpClientFactory,
            settings: Options.Create(new FhirBulkExporterSettings
            {
                StreamedExportHttpClientTimeout = streamedExportHttpClientTimeout
                                                   ?? new FhirBulkExporterSettings().StreamedExportHttpClientTimeout
            }));
    }

    public StubHttpMessageHandler Handler { get; }

    public FhirClient FhirClient { get; }

    public StubDateTimeProvider DateTimeProvider { get; }

    public StubFhirHttpClientFactory FhirHttpClientFactory { get; }

    public StubHttpClientFactory HttpClientFactory { get; }

    public IFhirBulkExporter Exporter { get; }

    /// <summary>
    /// Drives the exporter to the Completed state so that a GetExport test can start from a session that already
    /// holds the supplied manifest.
    /// </summary>
    public async Task<FhirBulkExportState> ArriveAtCompleted(
        string manifestJson,
        CancellationToken cancellationToken = default)
    {
        Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.KickOffAccepted());
        Handler.RespondTo(HttpMethod.Get, "$export-poll-status", () => HttpResponses.PollComplete(manifestJson));

        await Exporter.BeginExport(new Hl7.Fhir.Model.Parameters(), TestUrls.SourceRepositoryCode, cancellationToken);
        return await Exporter.PollExport(cancellationToken);
    }

    /// <summary>
    /// Drives the exporter to the InProgress state so that a poll or delete test starts from a live job.
    /// </summary>
    public async Task<FhirBulkExportState> ArriveAtInProgress(
        CancellationToken cancellationToken = default)
    {
        Handler.RespondTo(HttpMethod.Post, "$export", () => HttpResponses.KickOffAccepted());
        return await Exporter.BeginExport(new Hl7.Fhir.Model.Parameters(), TestUrls.SourceRepositoryCode, cancellationToken);
    }

    public void Dispose()
    {
        FhirClient.Dispose();
        Handler.Dispose();
    }
}
