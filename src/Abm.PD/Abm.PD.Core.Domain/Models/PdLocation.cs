namespace Abm.PD.Core.Domain.Models;

public class PdLocation(
    string resourceId,
    DateTimeOffset lastUpdated,
    PdOrganization managingOrganization,
    PdLocation? partOf,
    List<PdEndpoint> endpoint,
    int sourceResourceId) : PdResourceBase("Location", resourceId, lastUpdated, sourceResourceId)
{
    
    public PdOrganization ManagingOrganization { get; } = managingOrganization;
    public PdLocation? PartOf { get; } = partOf;
    public List<PdEndpoint> Endpoint { get; } = endpoint;
}