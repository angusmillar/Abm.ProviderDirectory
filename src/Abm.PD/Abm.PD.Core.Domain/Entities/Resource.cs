namespace Abm.PD.Core.Domain.Entities;

/// <summary>
/// Stub entity bootstrapping the provider directory database. Not yet backed by a specific FHIR
/// resource shape — ResourceType/ResourceId simply record what the resource is and its source
/// identifier, ahead of the real entity model this table will grow into.
/// </summary>
public class Resource
{
    public int Id { get; set; }

    public required string ResourceType { get; set; }

    public required string ResourceId { get; set; }
}
