namespace Abm.PD.Core.Domain.Models;

public class PdEndpoint(
    string resourceId,
    DateTimeOffset lastUpdated,
    int sourceResourceId) : PdResourceBase("Endpoint", resourceId, lastUpdated, sourceResourceId)
{
    
}