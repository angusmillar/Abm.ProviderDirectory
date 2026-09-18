using System.Text.Json;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using FhirResource = Hl7.Fhir.Model.Resource;
using Abm.PD.Core.Application.Extensions;
using Abm.PD.Core.Domain.Projections;

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

        Dictionary<string, SourceToTargetResourceIdLookup> sourceToTargetResourceIdDictionary = await sourceResourceRepository.
            GetSourceToTargetResourceIdDictionaryAsync(targetCorrelationId, cancellationToken);
        
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
                    
                    logger.LogInformation("Resource: {ResourceType}/{ResourceId} to target: {TargetResourceType}/{TargetResourceId}", 
                        resource.TypeName,  
                        resource.Id,
                        resource.TypeName,
                        sourceResource.TargetResourceId);
                    
                    List<ResourceReference> resourceReferenceList  = resource.GetAllResourceReferences();
                    resource.Id = sourceResource.TargetResourceId.ToString();
                    UpdateResourceReferences(sourceResource.Id, resourceReferenceList, sourceToTargetResourceIdDictionary);
                    
                    await sourceResourceRepository.UpdateResourceAsync(
                        id: sourceResource.Id, 
                        resource: await resource.ToJsonAsync(), 
                        cancellationToken: cancellationToken);
                    
                    processedCount++;
                }
                catch (Exception exception) when (exception is JsonException or ArgumentNullException)
                {
                    failedCount++;

                    logger.LogWarning(
                        exception,
                        "{ResourceType}/{ResourceId} for CorrelationId {CorrelationId} could not be deserialised",
                        sourceResource.ResourceType,
                        sourceResource.SourceResourceId,
                        targetCorrelationId);
                }
            }
        }

        return new MatchingTaskResult(ProcessedCount: processedCount, FailedCount: failedCount);
    }

    private void UpdateResourceReferences(
        int sourceResourceId,
        List<ResourceReference> resourceReferenceList,
        Dictionary<string, SourceToTargetResourceIdLookup> sourceResourceIdDictionary)
    {
        foreach (ResourceReference resourceReference in resourceReferenceList)
        {
            if (string.IsNullOrWhiteSpace(resourceReference.Reference))
            {
                continue;
            }
            
            if (!sourceResourceIdDictionary.TryGetValue(resourceReference.Reference.Trim(), out SourceToTargetResourceIdLookup? sourceResourceIdLookup))
            {
                logger.LogError("Found a resource reference which does not reference another resource from the same " +
                                "CorrectionID batch, unable to updates the reference's resource Id from its source to its target. " +
                                "Reference: {Reference}, found in {Entity}.id: {Id}",
                    resourceReference.Reference.Trim(), 
                    nameof(SourceResource), 
                    sourceResourceId);
                
                continue;
            }
            
            logger.LogInformation("  ResourceReference: {SourceResourceReference} to target: {TargetResourceReference}",
                resourceReference.Reference, 
                sourceResourceIdLookup.TargetResourceReference);
            
            resourceReference.Reference = sourceResourceIdLookup.TargetResourceReference;

        }
    }
}