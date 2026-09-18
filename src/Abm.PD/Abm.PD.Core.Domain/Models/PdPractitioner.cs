
namespace Abm.PD.Core.Domain.Models;

public class PdPractitioner(
    string resourceId,
    DateTimeOffset lastUpdated,
    int sourceResourceId) : PdResourceBase("Practitioner", resourceId, lastUpdated, sourceResourceId)
{
    
} 
