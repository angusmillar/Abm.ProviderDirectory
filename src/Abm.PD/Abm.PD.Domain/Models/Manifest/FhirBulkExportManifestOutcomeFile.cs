using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Abm.PD.Domain.Models.Manifest;

/// <summary>
/// An entry in the Output Manifest's "outcome" array (named "error" in v2.0.0), describing one file of
/// OperationOutcome resources reporting errors, warnings, information or success messages.
/// </summary>
public sealed record FhirBulkExportManifestOutcomeFile
{
    /// <summary>
    /// The resource type of the file's contents, which is always "OperationOutcome" when present. Optional (0..1).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Absolute URL of the file containing the OperationOutcome resources. Required (1..1).
    /// </summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>
    /// The number of OperationOutcome records in the file. Optional (0..1).
    /// </summary>
    [JsonPropertyName("count")]
    public long? Count { get; init; }

    /// <summary>
    /// The size of the file in bytes. Optional (0..1).
    /// </summary>
    [JsonPropertyName("fileSize")]
    public long? FileSize { get; init; }

    /// <summary>
    /// A breakdown of the file's OperationOutcome records by issue severity. Optional (0..*).
    /// </summary>
    [JsonPropertyName("countSeverity")]
    public IReadOnlyList<FhirBulkExportManifestOutcomeSeverityCount>? CountSeverity { get; init; }

    /// <summary>
    /// Server-defined custom behaviour. Optional (0..1).
    /// </summary>
    [JsonPropertyName("extension")]
    public JsonObject? Extension { get; init; }
}