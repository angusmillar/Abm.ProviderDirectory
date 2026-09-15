using Abm.PD.Core.Application.ExportTaskRunner;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// Always throws, so TaskScheduler.DoWork's catch block runs - used to assert FailureCount
// behaviour without a real FhirBulkExporter failure.
public sealed class ThrowingExportTaskRunner : IExportTaskRunner
{
    public Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated export failure");
    }
}
