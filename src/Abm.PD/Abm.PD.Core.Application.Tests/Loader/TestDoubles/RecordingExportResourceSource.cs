using System.Runtime.CompilerServices;
using Abm.PD.BulkExport.FhirBulkExport;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Tests.Loader.TestDoubles;

/// <summary>
/// Stands in for the exporter's streamed output, counting how many resources have actually been pulled from it -
/// the same technique Abm.PD.BulkExport.Tests uses to assert FhirBatchLoader's pipelining, reproduced here so
/// this test project does not need a reference onto another test project.
/// </summary>
public sealed class RecordingExportResourceSource(
    IReadOnlyList<FhirBulkExportResource> resources)
{
    private int Pulled;

    public int PulledCount => Volatile.Read(ref Pulled);

    public static RecordingExportResourceSource OfPractitioners(
        int count,
        int firstIdNumber = 1)
    {
        return new RecordingExportResourceSource(
            Enumerable.Range(firstIdNumber, count)
                .Select(idNumber => ExportResource(NewPractitioner(idNumber.ToString()), idNumber))
                .ToList());
    }

    public static Practitioner NewPractitioner(
        string? id,
        DateTimeOffset? lastUpdated = null)
    {
        return new Practitioner
        {
            Id = id,
            Meta = new Meta { LastUpdated = lastUpdated ?? DateTimeOffset.UtcNow },
        };
    }

    public static FhirBulkExportResource ExportResource(
        Resource resource,
        long lineNumber = 1)
    {
        return new FhirBulkExportResource(
            Resource: resource,
            ManifestOutputType: resource.TypeName,
            SourceUrl: new Uri("https://export.test/Practitioner.ndjson"),
            LineNumber: lineNumber);
    }

    public async IAsyncEnumerable<FhirBulkExportResource> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (FhirBulkExportResource resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            //Yielding the thread keeps the source genuinely asynchronous, so the loader's pipelining is exercised
            //rather than being hidden by a source that completes synchronously.
            await Task.Yield();

            Interlocked.Increment(ref Pulled);
            yield return resource;
        }
    }
}
