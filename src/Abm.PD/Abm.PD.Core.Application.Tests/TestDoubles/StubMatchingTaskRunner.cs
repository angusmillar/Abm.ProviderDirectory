using Abm.PD.Core.Application.MatchingTaskRunner;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

public sealed class StubMatchingTaskRunner(List<int> calls) : IMatchingTaskRunner
{
    public Task Run(
        MatchingTask matchingTask,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        calls.Add(matchingTask.Id);
        return Task.CompletedTask;
    }
}
