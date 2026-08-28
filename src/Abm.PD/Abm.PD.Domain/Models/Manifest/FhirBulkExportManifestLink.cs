using System.Text.Json.Serialization;

namespace Abm.PD.Domain.Models.Manifest;

/// <summary>
/// An entry in the Output Manifest's "link" array, pointing at a further page of the manifest when the client
/// permitted partial manifests.
/// </summary>
public sealed record FhirBulkExportManifestLink
{
    /// <summary>
    /// The relationship of the linked manifest to this one, which is "next" for manifest pagination. Required (1..1).
    /// </summary>
    [JsonPropertyName("relation")]
    public required string Relation { get; init; }

    /// <summary>
    /// Absolute URL of the linked manifest page. Required (1..1).
    /// </summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }
}