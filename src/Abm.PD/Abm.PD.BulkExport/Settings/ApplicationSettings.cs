using System.ComponentModel.DataAnnotations;

namespace Abm.PD.BulkExport.Settings;

public record ApplicationSettings
{
    public const string SectionName = "ApplicationSettings";

    [Required] public string ApplicationName { get; init; } = "Abm.PD.Console";

}