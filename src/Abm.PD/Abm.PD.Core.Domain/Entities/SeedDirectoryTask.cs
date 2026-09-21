using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Entities;

public class SeedDirectoryTask : TaskBase
{
    public SeedDirectoryTask()
        : base(TaskTypeId.SeedDirectoryTask)
    {
    }
    
    public required string MetaData { get; set; }
}
