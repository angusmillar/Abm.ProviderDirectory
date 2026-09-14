using Abm.PD.Core.Application.Loader;

namespace Abm.PD.Core.Application.ExportTaskRunner;

public interface IExportTaskRunner
{
    Task<SourceResourceLoadResult> Run(
        Domain.Entities.ExportTask exportTask,
        CancellationToken cancellationToken);
}
