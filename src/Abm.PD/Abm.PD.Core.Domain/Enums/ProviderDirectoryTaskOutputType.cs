using Abm.Core.Attributes;

namespace Abm.PD.Core.Domain.Enums;

public enum ProviderDirectoryTaskOutputType
{
    [EnumInfo("task-repetition-counter", "A task.output.type code for an integer value indicating how many times the task has been run.")]
    TaskRepetitionCounter,
}

public static class ProviderDirectoryTaskOutputTypeSystem
{
    public static Uri Uri = new Uri("http://corus-ix.au.fhir.telstrahealth.com/CodeSystem/pd-task-output-type");
}

