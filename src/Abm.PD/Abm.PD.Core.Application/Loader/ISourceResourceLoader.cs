using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.Loader;

public interface ISourceResourceLoader
{
    Task<SourceResourceLoadResult> Load(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        Guid correlationId,
        DataSource dataSource,
        CancellationToken cancellationToken);
}
