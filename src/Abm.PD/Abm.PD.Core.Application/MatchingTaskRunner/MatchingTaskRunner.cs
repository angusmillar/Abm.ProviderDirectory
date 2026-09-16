using System.Text.Json;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using FhirResource = Hl7.Fhir.Model.Resource;

namespace Abm.PD.Core.Application.MatchingTaskRunner;

public class MatchingTaskRunner(
    ILogger<MatchingTaskRunner> logger,
    ISourceResourceRepository sourceResourceRepository) : IMatchingTaskRunner
{
    // The provider directory resource types matching operates over, in the load order noted at the
    // top of ConsoleApplication.cs.
    private static readonly string[] ResourceTypes =
    [
        "Practitioner", "Endpoint", "Organization", "Location", "HealthcareService", "PractitionerRole",
    ];

    private static readonly JsonSerializerOptions FhirJsonSerializerOptions =
        new JsonSerializerOptions().ForFhir(typeof(ModelInfo).Assembly);

    public async Task<MatchingTaskResult> Run(
        MatchingTask matchingTask,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Running {TaskType}", matchingTask.TypeId);

        // MetaData carries the raw CorrelationId of the ExportTask run whose source_resource rows this
        // matching run targets - distinct from correlationId above, which identifies this matching run itself.
        Guid targetCorrelationId = Guid.Parse(matchingTask.MetaData);

        int processedCount = 0;
        int failedCount = 0;

        foreach (string resourceType in ResourceTypes)
        {
            await foreach (SourceResource sourceResource in
                           sourceResourceRepository.GetByCorrelationIdAsync(targetCorrelationId, resourceType, cancellationToken))
            {
                try
                {
                    FhirResource? resource = JsonSerializer.Deserialize<FhirResource>(
                        sourceResource.Resource, FhirJsonSerializerOptions);
                    ArgumentNullException.ThrowIfNull(resource);

                    logger.LogInformation("{ResourceType}/{ResourceId}", resource.TypeName,  resource.Id);
                    
                    processedCount++;
                }
                catch (Exception exception) when (exception is JsonException or ArgumentNullException)
                {
                    failedCount++;

                    logger.LogWarning(
                        exception,
                        "{ResourceType}/{ResourceId} for CorrelationId {CorrelationId} could not be deserialised",
                        sourceResource.ResourceType,
                        sourceResource.ResourceId,
                        targetCorrelationId);
                }
            }
        }

        return new MatchingTaskResult(ProcessedCount: processedCount, FailedCount: failedCount);
    }
}