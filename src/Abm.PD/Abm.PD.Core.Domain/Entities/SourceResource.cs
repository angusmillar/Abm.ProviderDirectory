namespace Abm.PD.Core.Domain.Entities;

public class SourceResource
{
    public int Id { get; set; }
    
    public required Guid CorrelationId { get; set; }

    public required string ResourceType { get; set; }

    public required string SourceResourceId { get; set; }

    // Populated by SourceResourceLoader with a new Version 7 GUID at load time. The FHIR JSON in Resource
    // still carries the source directory's resource.id - retargeting it, and the references that point to
    // it, is out of scope until the local repository insert step is written.
    public required Guid TargetResourceId { get; set; }

    public required DateTimeOffset ResourceLastUpdated { get; set; }
    
    public required int DataSourceId { get; set; }

    public required DataSource DataSource { get; set; }
    
    public required string Resource { get; set; }
    
    public required DateTime CreatedUtc { get; set; }

    public required DateTime UpdatedUtc { get; set; }

}