using Abm.PD.BulkExport.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application;

public interface IExportRunner
{
    Task<FhirBatchLoadResult> Run(
        ExportLoaderTask task,
        CancellationToken cancellationToken);
}
