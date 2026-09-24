using System.ComponentModel.DataAnnotations;

namespace Abm.PD.Core.Application.Settings;

public record ProviderDirectorySettings
{
    public const string SectionName = "ProviderDirectory";

    [Required]
    public required LocalFhirRepositoryCodes LocalFhirRepositoryCodes { get; init; }
    
    [Required]
    public required ExternalFhirRepositoryCodes ExternalFhirRepositoryCodes { get; init; }

    [Required]
    public required FhirSystemUriSettings FhirSystemUriSettings { get; init; }
}

/// <summary>
/// Maps each External provider directory role this application plays to the Code of the FhirNavigator
/// repository that fills it, so the same FhirNavigator.FhirRepositories list can serve source and
/// target directories without the application hard-coding which repository is which.
/// </summary>
public record ExternalFhirRepositoryCodes
{
    [Required]
    public required string HealthConnectProviderDirectory { get; init; }
    
    [Required]
    public required string HealthLinkProviderDirectory { get; init; }
    
}

/// <summary>
/// Maps each Local provider directory role this application plays to the Code of the FhirNavigator
/// repository that fills it, so the same FhirNavigator.FhirRepositories list can serve source and
/// target directories without the application hard-coding which repository is which.
/// </summary>
public record LocalFhirRepositoryCodes
{
    [Required]
    public required string TelstraHealthProviderDirectory { get; init; }

    [Required]
    public required string HealthConnectProviderDirectory { get; init; }
    
    [Required]
    public required string HealthLinkProviderDirectory { get; init; }
    
}

public record FhirSystemUriSettings
{
    [Required]
    public required Uri ProviderDirectoryIgBaseUrl { get; init; }
    
}
