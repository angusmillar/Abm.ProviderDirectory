namespace Abm.PD.Core.Domain.Models;

public abstract class PdResourceBase(
    string resourceType,
    string resourceId,
    DateTimeOffset lastUpdated,
    int sourceResourceId)
{
    public string ResourceType { get; internal set; } = resourceType;

    public string ResourceId { get; internal set; } = resourceId;

    public DateTimeOffset LastUpdated { get; internal set; } = lastUpdated;
    
    public int SourceResourceId { get; internal set; } = sourceResourceId;
    
}