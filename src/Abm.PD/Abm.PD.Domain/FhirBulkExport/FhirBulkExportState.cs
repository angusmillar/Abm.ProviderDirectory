using Abm.PD.Domain.Models.Manifest;
using Hl7.Fhir.Model;

namespace Abm.PD.Domain.FhirBulkExport;

public record FhirBulkExportState(
    FhirBulkExportSessionStatus SessionStatus,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    string? JobId,
    OperationOutcome? OperationOutcome,
    string[]? ErrorMessages,
    FhirBulkExportManifest? Manifest);