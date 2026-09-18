namespace Abm.PD.Core.Domain.Models;

public class PdCoverageAreaLocation(
    string resourceId,
    DateTimeOffset lastUpdated,
    PdOrganization? managingOrganization,
    int sourceResourceId) : PdResourceBase("Location", resourceId, lastUpdated, sourceResourceId)
{
    public PdOrganization? ManagingOrganization { get; set; } = managingOrganization;
}