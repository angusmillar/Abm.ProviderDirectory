using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Entities;

public class ExportLoaderTask : TaskBase 
{
    public override TaskTypeId TypeId => TaskTypeId.BulkImport;
    public override required string Code { get; set; }
    public override required string DisplayName { get; set; }
    public override string? Description { get; set; }
    public required ExportParameter Parameter { get; set; } 
    
}