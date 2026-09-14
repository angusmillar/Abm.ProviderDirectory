using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// Always throws, so TaskScheduler.DoWork's catch block runs - used to assert FailureCount
// behaviour without a real FhirBulkExporter failure.
public sealed class ThrowingExportRunner : IExportRunner
{
    public Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated export failure");
    }
}
