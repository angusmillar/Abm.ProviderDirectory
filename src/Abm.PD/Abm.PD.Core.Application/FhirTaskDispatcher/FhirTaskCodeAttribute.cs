namespace Abm.PD.Core.Application.FhirTaskDispatcher;

// Ties a FhirTaskHandlerType value to the FHIR Task code (from FhirTaskCodeSystemsUri) that selects it,
// so the code-to-handler association lives with the enum rather than in a separate lookup table.
[AttributeUsage(AttributeTargets.Field)]
public sealed class FhirTaskCodeAttribute(
    string code
    ) : Attribute
{
    public string Code { get; } = code;
}
