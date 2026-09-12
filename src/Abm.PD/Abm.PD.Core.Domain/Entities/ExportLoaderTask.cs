using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Entities;

public class ExportLoaderTask : TaskBase
{
    public ExportLoaderTask()
        : base(TaskTypeId.BulkImport)
    {
    }

    public required ExportParameter Parameter { get; set; }
}
