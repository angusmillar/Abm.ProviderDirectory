namespace Abm.PD.Domain.Loader;

/// <summary>
/// One resource the target server refused, held with enough of its origin to find it again in the export.
/// </summary>
public sealed record FhirBatchLoadFailure(
    string ResourceType,
    string? ResourceId,
    Uri SourceUrl,
    long LineNumber,
    int? HttpStatusCode,
    string[] ErrorMessages);
