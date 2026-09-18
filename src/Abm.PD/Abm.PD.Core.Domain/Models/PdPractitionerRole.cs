namespace Abm.PD.Core.Domain.Models;

public class PdPractitionerRole(
    string resourceId,
    DateTimeOffset lastUpdated,
    int sourceResourceId,
    PdPractitioner practitioner,
    PdOrganization organization,
    PdLocation location,
    PdHealthcareService healthcareService,
    List<PdEndpoint> endpoint) : PdResourceBase("PractitionerRole", resourceId, lastUpdated, sourceResourceId)
{
    public PdPractitioner Practitioner { get; } = practitioner;
    public PdOrganization Organization { get; } = organization;
    public PdLocation Location { get; } = location;
    public PdHealthcareService HealthcareService { get; } = healthcareService;
    public List<PdEndpoint> Endpoint { get; } = endpoint;
}