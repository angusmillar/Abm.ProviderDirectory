using System.ComponentModel.DataAnnotations;

namespace Abm.PD.Console.Settings;

public record ConsoleApplicationSettings
{
    public const string SectionName = "ApplicationSettings";

    [Required] public string ApplicationName { get; init; } = "Abm.PD.Console";

}