using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Domain.Loader;
using Abm.PD.Domain.Settings;
using System.Text.Json;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Abm.PD.Tests.TestDoubles;

/// <summary>
/// Builds a real <see cref="FhirBatchLoader"/> whose only outbound seam, the Firely FhirClient it commits each
/// batch through, terminates in the test's <see cref="StubHttpMessageHandler"/>.
///
/// As with the exporter's harness the FhirClient is not faked: it is the piece serialising the batch Bundle and
/// deserialising the batch-response the loader reads its per entry outcomes from, so faking it would only test
/// the fake.
/// </summary>
public sealed class FhirBatchLoaderHarness : IDisposable
{
    public FhirBatchLoaderHarness(
        int batchSize = 2,
        int maxRetainedFailures = 100)
    {
        Handler = new StubHttpMessageHandler();

        FhirClient = new FhirClient(
            endpoint: new Uri(TestUrls.TargetServiceBaseUrl),
            settings: new FhirClientSettings
            {
                PreferredFormat = ResourceFormat.Json,
                PreferredParameterHandling = SearchParameterHandling.Lenient
            },
            messageHandler: Handler);

        FhirHttpClientFactory = new StubFhirHttpClientFactory(FhirClient);

        Loader = new FhirBatchLoader(
            logger: NullLogger<FhirBatchLoader>.Instance,
            settings: Options.Create(new FhirBatchLoaderSettings
            {
                BatchSize = batchSize,
                MaxRetainedFailures = maxRetainedFailures
            }),
            fhirHttpClientFactory: FhirHttpClientFactory);
    }

    public StubHttpMessageHandler Handler { get; }

    public FhirClient FhirClient { get; }

    public StubFhirHttpClientFactory FhirHttpClientFactory { get; }

    public IFhirBatchLoader Loader { get; }

    /// <summary>
    /// Scripts every batch commit with the same response, built fresh per call so a route can be hit by more
    /// than one batch.
    /// </summary>
    public FhirBatchLoaderHarness RespondToEveryCommit(
        Func<HttpResponseMessage> respond)
    {
        Handler.RespondTo(
            predicate: request => request.Method == HttpMethod.Post,
            respond: _ => respond());

        return this;
    }

    /// <summary>
    /// Answers every entry of every batch with 200 OK.
    /// </summary>
    public FhirBatchLoaderHarness RespondToEveryCommitWithSuccess()
    {
        Handler.RespondTo(
            predicate: request => request.Method == HttpMethod.Post,
            respond: _ => HttpResponses.BatchResponseAll("200 OK", CountEntriesOfLastRequest()));

        return this;
    }

    /// <summary>
    /// The batch Bundles the loader has committed, in order, read back from the recorded request bodies.
    /// </summary>
    public IReadOnlyList<Bundle> CommittedBundles()
    {
        return Handler.ReceivedRequests
            .Where(request => request.Method == HttpMethod.Post && request.Body is not null)
            .Select(request => BundleFromJson(request.Body!))
            .ToList();
    }

    public void Dispose()
    {
        FhirClient.Dispose();
        Handler.Dispose();
    }

    //The FHIR POCOs need Firely's converters, as they do in the exporter, so the committed bundle is read back
    //with the same serializer configuration the loader wrote it with.
    private static readonly JsonSerializerOptions FhirJsonSerializerOptions =
        new JsonSerializerOptions().ForFhir(typeof(ModelInfo).Assembly);

    private static Bundle BundleFromJson(
        string json)
    {
        return JsonSerializer.Deserialize<Bundle>(json, FhirJsonSerializerOptions)
               ?? throw new InvalidOperationException("The committed request body was not a Bundle.");
    }

    private int CountEntriesOfLastRequest()
    {
        //A batch-response has to carry one entry per request entry, so the count comes off the request the
        //loader actually sent rather than being assumed by the test. The handler records a request before it
        //runs the route, so the last recorded one is the request being answered here.
        string json = Handler.ReceivedRequests[^1].Body
                      ?? throw new InvalidOperationException("The batch commit carried no body.");

        return BundleFromJson(json).Entry.Count;
    }
}
