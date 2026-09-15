using Abm.PD.Core.Application.ExportTaskRunner;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// Registered Scoped in the test's ServiceCollection so a fresh InstanceId is minted once per
// CreateScope() call - exactly the behaviour Finding 1's regression test is asserting on.
public sealed class ScopeTrackingExportTaskRunner(List<(int TaskId, Guid InstanceId)> calls) : IExportTaskRunner
{
    private readonly Guid InstanceId = Guid.NewGuid();

    public Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        calls.Add((exportTask.Id, InstanceId));
        return Task.FromResult(new SourceResourceLoadResult(
            SubmittedCount: 0, CommittedCount: 0, FailedCount: 0, BatchCount: 0, RetainedFailures: []));
    }
}
