namespace Abm.PD.Core.Domain.Entities;

public class SourceResource
{
    public int Id { get; set; }
    
    public required Guid CorrelationId { get; set; }

    public required string ResourceType { get; set; }
    
    public required string ResourceId { get; set; }
    
    public required DateTimeOffset ResourceLastUpdated { get; set; }
    
    public required int DataSourceId { get; set; }

    public required DataSource DataSource { get; set; }
    
    public required string Resource { get; set; }
    
    public required DateTime CreatedUtc { get; set; }

    public required DateTime UpdatedUtc { get; set; }

}