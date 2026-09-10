using Abm.PD.BulkExport.Models.Manifest;
using Hl7.Fhir.Model;

namespace Abm.PD.BulkExport.FhirBulkExport;

public record FhirBulkExportState(
    FhirBulkExportSessionStatus SessionStatus,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    string? JobId,
    string? ProgressMessage,
    TimeSpan? RequestedPollDelay,
    OperationOutcome? OperationOutcome,
    string[]? ErrorMessages,
    FhirBulkExportManifest? Manifest);