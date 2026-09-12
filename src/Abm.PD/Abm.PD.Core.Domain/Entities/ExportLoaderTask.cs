namespace Abm.PD.Core.Domain.Entities;

public class ExportLoaderTask : TaskBase
{
    public required ExportParameter Parameter { get; set; }
}
