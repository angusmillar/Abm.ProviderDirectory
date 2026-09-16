using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.MatchingTaskRunner;

public interface IMatchingTaskRunner
{
    Task<MatchingTaskResult> Run(
        MatchingTask matchingTask,
        Guid correlationId,
        CancellationToken cancellationToken);
}
