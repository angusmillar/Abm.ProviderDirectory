using System.Text.Json.Serialization;

namespace Abm.PD.Domain.Models.Manifest;

/// <summary>
/// A count of the OperationOutcome records in an outcome file that carry a given issue severity.
/// </summary>
public sealed record FhirBulkExportManifestOutcomeSeverityCount
{
    /// <summary>
    /// The severity code: fatal, error, warning, information or success. Required (1..1).
    /// Left as a string so that an unexpected code from a server does not fail deserialization.
    /// </summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>
    /// The number of OperationOutcome records at this severity. Required (1..1).
    /// </summary>
    [JsonPropertyName("count")]
    public long Count { get; init; }
}