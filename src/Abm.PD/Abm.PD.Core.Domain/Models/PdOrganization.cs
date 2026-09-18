namespace Abm.PD.Core.Domain.Models;

public class PdOrganization(
    string resourceId,
    DateTimeOffset lastUpdated,
    PdOrganization? partOf,
    int sourceResourceId) : PdResourceBase("Organization", resourceId, lastUpdated, sourceResourceId)
{
    
    public PdOrganization? PartOf { get; } = partOf;
}