using System.ComponentModel.DataAnnotations;

namespace Abm.PD.Core.Application.Settings;

public record ProviderDirectorySettings
{
    public const string SectionName = "ProviderDirectory";

    [Required]
    public required FhirRepositoryCodeAssignmentSettings FhirRepositoryCodeAssignment { get; init; }
}

/// <summary>
/// Maps each provider directory role this application plays to the Code of the FhirNavigator
/// repository that fills it, so the same FhirNavigator.FhirRepositories list can serve source and
/// target directories without the application hard-coding which repository is which.
/// </summary>
public record FhirRepositoryCodeAssignmentSettings
{
    [Required]
    public required string HealthConnectProviderDirectorySource { get; init; }

    [Required]
    public required string HealthLinkProviderDirectorySource { get; init; }

    [Required]
    public required string TelstraHealthProviderDirectoryTarget { get; init; }
}
