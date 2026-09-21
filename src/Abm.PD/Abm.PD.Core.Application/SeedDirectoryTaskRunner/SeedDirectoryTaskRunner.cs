using System.Text.Json;
using Abm.PD.BulkExport.Loader;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using FhirResource = Hl7.Fhir.Model.Resource;
using Abm.PD.Core.Application.Extensions;
using Abm.PD.Core.Domain.Projections;

namespace Abm.PD.Core.Application.SeedDirectoryTaskRunner;

public class SeedDirectoryTaskRunner(
    ILogger<SeedDirectoryTaskRunner> logger,
    ISourceResourceRepository sourceResourceRepository) : ISeedDirectoryTaskRunner
{
    // The provider directory resource types matching operates over, in the load order noted at the
    // top of ConsoleApplication.cs.
    private static readonly string[] ResourceTypes =
    [
        "Practitioner", "Endpoint", "Organization", "Location", "HealthcareService", "PractitionerRole",
    ];

    private static readonly JsonSerializerOptions FhirJsonSerializerOptions =
        new JsonSerializerOptions().ForFhir(typeof(ModelInfo).Assembly);

    public async Task<SeedDirectoryTaskResult> Run(
        SeedDirectoryTask seedDirectoryTask,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Running {TaskType}", seedDirectoryTask.TypeId);

        // MetaData carries the raw CorrelationId of the ExportTask run whose source_resource rows this
        // matching run targets - distinct from correlationId above, which identifies this matching run itself.
        Guid targetCorrelationId = Guid.Parse(seedDirectoryTask.MetaData);

        int processedCount = 0;
        int failedCount = 0;

        var sourceToTargetResourceIdDictionary = await GetSourceToTargetResourceIdDictionary(cancellationToken, targetCorrelationId);

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

                    // The resource's own target id comes from the same dictionary as every reference it holds
                    // to another resource - it was built for this whole correlation, so this row's own key is
                    // in it too.
                    string ownKey = $"{sourceResource.ResourceType}/{sourceResource.ResourceId}";
                    if (!sourceToTargetResourceIdDictionary.TryGetValue(ownKey, out SourceToTargetResourceIdLookup? ownLookup))
                    {
                        logger.LogError("Found no target resource id for {ResourceType}/{ResourceId}, unable to retarget it",
                            sourceResource.ResourceType,
                            sourceResource.ResourceId);

                        failedCount++;
                        continue;
                    }

                    // Set for every entry, including this one, immediately after the dictionary was built above.
                    string targetResourceId = ownLookup.AssignedTargetResourceId!;

                    logger.LogInformation("Resource: {ResourceType}/{ResourceId} to target: {TargetResourceType}/{TargetResourceId}",
                        resource.TypeName,
                        resource.Id,
                        resource.TypeName,
                        targetResourceId);

                    List<ResourceReference> resourceReferenceList  = resource.GetAllResourceReferences();
                    resource.Id = targetResourceId;
                    UpdateResourceReferences(sourceResource.Id, resourceReferenceList, sourceToTargetResourceIdDictionary);

                    // Disabled until the target directory write is designed - see SeedDirectoryTaskRunner's remit
                    // for this development cycle.
                    // await sourceResourceRepository.UpdateResourceAsync(
                    //     id: sourceResource.Id,
                    //     resource: await resource.ToJsonAsync(),
                    //     cancellationToken: cancellationToken);

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

        return new SeedDirectoryTaskResult(ProcessedCount: processedCount, FailedCount: failedCount);
    }

    private async Task<Dictionary<string, SourceToTargetResourceIdLookup>> GetSourceToTargetResourceIdDictionary(
        CancellationToken cancellationToken,
        Guid targetCorrelationId)
    {
        Dictionary<string, SourceToTargetResourceIdLookup> sourceToTargetResourceIdDictionary = await sourceResourceRepository.
            GetSourceToTargetResourceIdDictionaryAsync(targetCorrelationId, cancellationToken);

        // SourceResource carries no AssignedTargetResourceId column, so every entry comes back from the
        // repository with AssignedTargetResourceId null - see SourceToTargetResourceIdLookup's doc comment.
        // Generating it here, explicitly, keeps it obvious that these ids are made up on the fly for this run
        // and not read from the database.
        foreach (SourceToTargetResourceIdLookup lookup in sourceToTargetResourceIdDictionary.Values)
        {
            lookup.AssignedTargetResourceId = Guid.CreateVersion7().ToString();
        }

        return sourceToTargetResourceIdDictionary;
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
            
            // Set for every entry immediately after the dictionary was built in Run, above.
            string targetResourceReference = $"{sourceResourceIdLookup.ResourceType}/{sourceResourceIdLookup.AssignedTargetResourceId}";

            logger.LogInformation("  ResourceReference: {SourceResourceReference} to target: {TargetResourceReference}",
                resourceReference.Reference,
                targetResourceReference);

            resourceReference.Reference = targetResourceReference;

        }
    }
}