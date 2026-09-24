namespace Abm.PD.Core.Application.FhirTaskDispatcher;

public record TaskHandlerOutcome(
    Hl7.Fhir.Model.Task.TaskStatus TaskStatus, 
    string? StatusReason);