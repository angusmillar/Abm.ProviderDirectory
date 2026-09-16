using Abm.PD.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Abm.PD.Core.Application.MatchingTaskRunner;

public class MatchingTaskRunner(
    ILogger<MatchingTaskRunner> logger)
{
    public Task Run(MatchingTask matchingTask,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        
        logger.LogInformation("Running {TaskType}", matchingTask.TypeId);
        
        
        
        throw new NotImplementedException();
        
    }
}