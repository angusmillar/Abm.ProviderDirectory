using Hl7.Fhir.Model;

namespace Abm.PD.Domain.FhirBulkExport;

/// <summary>
/// A single FHIR resource read from one of the export's NDJSON output files, with enough of its origin retained
/// to trace it back to the exact line of the exact file it came from.
/// </summary>
public sealed record FhirBulkExportResource(
    Resource Resource,
    string? ManifestOutputType,
    Uri SourceUrl,
    long LineNumber);
