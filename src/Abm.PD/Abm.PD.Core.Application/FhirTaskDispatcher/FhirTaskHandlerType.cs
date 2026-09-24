using Abm.Core.Attributes;

namespace Abm.PD.Core.Application.FhirTaskDispatcher;

public enum FhirTaskHandlerType
{
    [EnumInfo("seed-provider-directory-resources", "A task to seed resources into a new Provider Directory")]
    SeedProviderDirectory,
    [EnumInfo("merge-and-update-directory", "A task to Merge & Update source resources into the Provider Directory")]
    MergeAndUpdateDirectory
}

public static class FhirTaskHandlerTypeSystem
{
    public static Uri Uri = new Uri("http://corus-ix.au.fhir.telstrahealth.com/CodeSystem/pd-task-code");
}

