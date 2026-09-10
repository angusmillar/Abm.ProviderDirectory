using System.ComponentModel.DataAnnotations;

namespace Abm.PD.BulkExport.Settings;

public record FhirDiskWriterSettings
{
    public const string SectionName = "FhirDiskWriter";

    /// <summary>
    /// The local directory path that the FHIR Disk Writer will write the exported FHIR resource into.
    /// file format: [ResourceType]-[Resource.id] 
    /// </summary>
    public string OutputDirectoryPath { get; init; } = string.Empty;

}
