using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Abm.PD.Domain.Models.Manifest;

/// <summary>
/// An entry in the Output Manifest's "output" array, describing one generated data file.
/// </summary>
public sealed record FhirBulkExportManifestOutputFile
{
    /// <summary>
    /// The FHIR resource type contained in the file. Required when the manifest has no outputOrganizedBy value,
    /// otherwise optional (0..1).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Absolute URL of the file. Required (1..1).
    /// </summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>
    /// URL of the next file, when a block of resources spans more than one file. Optional (0..1).
    /// </summary>
    [JsonPropertyName("continuesInFile")]
    public string? ContinuesInFile { get; init; }

    /// <summary>
    /// The number of resources in the file. Optional (0..1).
    /// </summary>
    [JsonPropertyName("count")]
    public long? Count { get; init; }

    /// <summary>
    /// The size of the file in bytes. Optional (0..1).
    /// </summary>
    [JsonPropertyName("fileSize")]
    public long? FileSize { get; init; }

    /// <summary>
    /// Server-defined custom behaviour. Optional (0..1).
    /// </summary>
    [JsonPropertyName("extension")]
    public JsonObject? Extension { get; init; }
}