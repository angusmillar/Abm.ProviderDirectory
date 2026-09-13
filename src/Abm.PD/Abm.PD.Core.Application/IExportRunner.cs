using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application;

public interface IExportRunner
{
    Task<SourceResourceLoadResult> Run(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken);
}
