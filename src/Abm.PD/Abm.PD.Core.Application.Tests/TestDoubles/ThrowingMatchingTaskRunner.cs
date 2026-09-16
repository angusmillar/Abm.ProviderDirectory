using Abm.PD.Core.Application.MatchingTaskRunner;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// Always throws, so TaskScheduler.DoWork's catch block runs - used to assert FailureCount
// behaviour without a real matching implementation, mirroring ThrowingExportTaskRunner.
public sealed class ThrowingMatchingTaskRunner : IMatchingTaskRunner
{
    public Task Run(
        MatchingTask matchingTask,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated matching failure");
    }
}
