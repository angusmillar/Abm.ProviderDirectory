using System.Runtime.CompilerServices;
using Abm.PD.BulkExport.FhirBulkExport;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.BulkExport.Tests.TestDoubles;

/// <summary>
/// Stands in for the exporter's streamed output, counting how many resources have actually been pulled from it.
///
/// That count is how the loader's pipelining is asserted: while a batch commit is still in flight the source
/// must keep being read, because a stalled read is what lets an intermediary decide the export's download
/// connection has gone idle. The same count also proves the read does not run away — only one batch may be
/// gathered ahead of the commit.
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
                .Select(idNumber => ExportResource(new Practitioner { Id = idNumber.ToString() }, idNumber))
                .ToList());
    }

    public static FhirBulkExportResource ExportResource(
        Resource resource,
        long lineNumber = 1,
        string? sourceUrl = TestUrls.PractitionerOutputFileUrl)
    {
        return new FhirBulkExportResource(
            Resource: resource,
            ManifestOutputType: resource.TypeName,
            SourceUrl: new Uri(sourceUrl ?? TestUrls.PractitionerOutputFileUrl),
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
