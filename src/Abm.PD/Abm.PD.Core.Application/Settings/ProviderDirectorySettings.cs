using System.ComponentModel.DataAnnotations;

namespace Abm.PD.Core.Application.Settings;

public record ProviderDirectorySettings
{
    public const string SectionName = "ProviderDirectory";

    [Required]
    public required FhirRepositoryCodeAssignmentSettings FhirRepositoryCodeAssignment { get; init; }
    
    [Required]
    public required FhirSystemUriSettings FhirSystemUriSettings { get; init; }
}

/// <summary>
/// Maps each provider directory role this application plays to the Code of the FhirNavigator
/// repository that fills it, so the same FhirNavigator.FhirRepositories list can serve source and
/// target directories without the application hard-coding which repository is which.
/// </summary>
public record FhirRepositoryCodeAssignmentSettings
{
    [Required]
    public required string HealthConnectProviderDirectoryExternal { get; init; }
    
    [Required]
    public required string HealthConnectProviderDirectoryLocal { get; init; }

    [Required]
    public required string HealthLinkProviderDirectoryExternal { get; init; }

    [Required]
    public required string HealthLinkProviderDirectoryLocal { get; init; }

    [Required]
    public required string TelstraHealthProviderDirectoryTarget { get; init; }
    
}

public record FhirSystemUriSettings
{
    [Required]
    public required Uri ProviderDirectoryIgBaseUrl { get; init; }
    
}
