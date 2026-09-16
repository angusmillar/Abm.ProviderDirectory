using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.MatchingTaskRunner;

public interface IMatchingTaskRunner
{
    Task Run(
        MatchingTask matchingTask,
        Guid correlationId,
        CancellationToken cancellationToken);
}
