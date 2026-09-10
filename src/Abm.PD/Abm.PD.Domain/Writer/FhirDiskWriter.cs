using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Domain.Settings;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Domain.Writer;

public class FhirDiskWriter(
    ILogger<FhirDiskWriter> logger,
    IOptions<FhirDiskWriterSettings> settings) : IFhirDiskWriter
{
    public DirectoryInfo OutputDirectoryInfo { get; } = new(settings.Value.OutputDirectoryPath);

    public async Task Write(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        CancellationToken cancellationToken)
    {
        int counter = 0;
        await foreach (FhirBulkExportResource exportResource in exportResources.WithCancellation(cancellationToken))
        {
            counter++;
            await ProcessResource(resourceCount: counter, fhirBulkExportResource: exportResource, cancellationToken);
        }
    }

    private async Task ProcessResource(
        int resourceCount,
        FhirBulkExportResource fhirBulkExportResource,
        CancellationToken cancellationToken)
    {
        LogResource(fhirBulkExportResource);

        await File.WriteAllTextAsync(Path.Combine(
                OutputDirectoryInfo.FullName,
                $"{fhirBulkExportResource.Resource.TypeName}-{fhirBulkExportResource.Resource.Id}.json"),
            await fhirBulkExportResource.Resource.ToJsonAsync(), cancellationToken);
    }

    private void LogResource(
        FhirBulkExportResource resource)
    {
        logger.LogInformation("{ResourceType}/{ResourceId} read from line {LineNumber} of {SourceUrl}",
            resource.Resource.TypeName,
            resource.Resource.Id,
            resource.LineNumber,
            resource.SourceUrl);
    }
}