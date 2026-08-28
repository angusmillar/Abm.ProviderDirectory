using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Abm.PD.Domain.Models.Manifest;

/// <summary>
/// An entry in the Output Manifest's "deleted" array, describing one file of transaction Bundles that carry
/// delete requests for resources removed since the requested _since instant.
/// </summary>
public sealed record FhirBulkExportManifestDeletedFile
{
    /// <summary>
    /// The resource type of the file's contents, which is always "Bundle" when present. Optional (0..1).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Absolute URL of the file containing the deletion records. Required (1..1).
    /// </summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>
    /// The number of deleted resource records in the file. Optional (0..1).
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