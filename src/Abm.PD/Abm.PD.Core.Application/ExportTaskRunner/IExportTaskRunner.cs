using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.ExportTaskRunner;

public interface IExportTaskRunner
{
    Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        Guid correlationId,
        CancellationToken cancellationToken);
}
