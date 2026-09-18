namespace Abm.PD.Core.Domain.Models;

public class PdHealthcareService(
    string resourceId,
    DateTimeOffset lastUpdated,
    PdOrganization providedBy,
    PdLocation location,
    List<PdLocation> coverageArea,
    List<PdEndpoint> endpoint,
    int sourceResourceId) : PdResourceBase("HealthcareService", resourceId, lastUpdated, sourceResourceId)
{
    public PdOrganization ProvidedBy { get; } = providedBy;
    public PdLocation Location { get; } = location;
    public List<PdLocation> CoverageArea { get; } = coverageArea;
    public List<PdEndpoint> Endpoint { get; } = endpoint;
}