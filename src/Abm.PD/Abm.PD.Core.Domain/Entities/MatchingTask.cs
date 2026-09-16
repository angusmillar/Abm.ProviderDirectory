using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Entities;

public class MatchingTask : TaskBase
{
    public MatchingTask()
        : base(TaskTypeId.MatchingTask)
    {
    }
    
    public required string MetaData { get; set; }
}
