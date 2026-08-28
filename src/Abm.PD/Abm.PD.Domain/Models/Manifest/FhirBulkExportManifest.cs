using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Abm.PD.Domain.Models.Manifest;

/// <summary>
/// The Output Manifest returned in the body of a 200 OK response from the bulk data export status endpoint.
/// See https://build.fhir.org/ig/HL7/bulk-data/en/export.html#response---output-manifest
/// </summary>
public sealed record FhirBulkExportManifest
{
    /// <summary>
    /// Canonical URL of the logical model that defines the structure of this manifest. Optional (0..1).
    /// </summary>
    [JsonPropertyName("manifestType")]
    public string? ManifestType { get; init; }

    /// <summary>
    /// The Data Provider's time when the query was run or the files were generated. Required (1..1).
    /// </summary>
    [JsonPropertyName("transactionTime")]
    public DateTimeOffset TransactionTime { get; init; }

    /// <summary>
    /// The full URL of the original bulk data kick-off request. Present in v2.0.0 manifests; absent from the
    /// current ballot. Optional (0..1).
    /// </summary>
    [JsonPropertyName("request")]
    public string? Request { get; init; }

    /// <summary>
    /// Indicates whether the file URLs require an access token to download. Required (1..1).
    /// </summary>
    [JsonPropertyName("requiresAccessToken")]
    public bool RequiresAccessToken { get; init; }

    /// <summary>
    /// MIME type of the bulk data files. Defaults to application/fhir+ndjson when absent. Optional (0..1).
    /// </summary>
    [JsonPropertyName("outputFormat")]
    public string? OutputFormat { get; init; }

    /// <summary>
    /// The resource type the output files are organised by, for example "Patient". Optional (0..1).
    /// </summary>
    [JsonPropertyName("outputOrganizedBy")]
    public string? OutputOrganizedBy { get; init; }

    /// <summary>
    /// Narrative detail describing how the output is organised by <see cref="OutputOrganizedBy"/>. Optional (0..1).
    /// </summary>
    [JsonPropertyName("outputOrganizedByDetail")]
    public string? OutputOrganizedByDetail { get; init; }

    /// <summary>
    /// The generated data files. Empty or absent when the export produced no resources. Optional (0..*).
    /// </summary>
    [JsonPropertyName("output")]
    public IReadOnlyList<FhirBulkExportManifestOutputFile>? Output { get; init; }

    /// <summary>
    /// Files of transaction Bundles containing delete requests, populated when _since was supplied. Optional (0..*).
    /// </summary>
    [JsonPropertyName("deleted")]
    public IReadOnlyList<FhirBulkExportManifestDeletedFile>? Deleted { get; init; }

    /// <summary>
    /// Files of OperationOutcome resources reporting errors, warnings and information. Optional (0..*).
    /// </summary>
    [JsonPropertyName("outcome")]
    public IReadOnlyList<FhirBulkExportManifestOutcomeFile>? Outcome { get; init; }

    /// <summary>
    /// The v2.0.0 name for <see cref="Outcome"/>, retained so manifests from servers implementing the published
    /// STU2 specification still deserialize. Optional (0..*).
    /// </summary>
    [JsonPropertyName("error")]
    public IReadOnlyList<FhirBulkExportManifestOutcomeFile>? Error { get; init; }

    /// <summary>
    /// Pagination links to further manifest pages. Only populated when partial manifests were permitted, and then
    /// carries a single entry with a relation of "next". Optional (0..*).
    /// </summary>
    [JsonPropertyName("link")]
    public IReadOnlyList<FhirBulkExportManifestLink>? Link { get; init; }

    /// <summary>
    /// Server-defined custom behaviour. The specification reserves this name and never defines a field within it,
    /// so it is left as an untyped JSON object. Optional (0..1).
    /// </summary>
    [JsonPropertyName("extension")]
    public JsonObject? Extension { get; init; }
}
