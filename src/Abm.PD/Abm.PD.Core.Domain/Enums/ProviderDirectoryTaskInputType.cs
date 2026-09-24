using Abm.Core.Attributes;

namespace Abm.PD.Core.Domain.Enums;

public enum ProviderDirectoryTaskInputType
{
    [EnumInfo("fhir-bulk-export-request-parameters", "A task.input.type code for a FHIR Bulk Export Parameters resources reference")]
    FhirBulkExportRequestParameters,
    [EnumInfo("task-repetition-counter", "A task.output.type code for an integer value indicating how many times the task has been run.")]
    TaskRepetitionCounter,
}

public static class ProviderDirectoryTaskInputTypeSystem
{
    public static Uri Uri = new Uri("http://corus-ix.au.fhir.telstrahealth.com/CodeSystem/pd-task-input-type");
}

