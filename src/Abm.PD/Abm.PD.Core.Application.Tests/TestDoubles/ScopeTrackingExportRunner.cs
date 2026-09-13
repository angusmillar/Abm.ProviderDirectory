using Abm.PD.BulkExport.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// Registered Scoped in the test's ServiceCollection so a fresh InstanceId is minted once per
// CreateScope() call - exactly the behaviour Finding 1's regression test is asserting on.
public sealed class ScopeTrackingExportRunner(List<(int TaskId, Guid InstanceId)> calls) : IExportRunner
{
    private readonly Guid InstanceId = Guid.NewGuid();

    public Task<FhirBatchLoadResult> Run(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        calls.Add((exportLoaderTask.Id, InstanceId));
        return Task.FromResult(new FhirBatchLoadResult(
            SubmittedCount: 0, CommittedCount: 0, FailedCount: 0, BatchCount: 0, RetainedFailures: []));
    }
}
