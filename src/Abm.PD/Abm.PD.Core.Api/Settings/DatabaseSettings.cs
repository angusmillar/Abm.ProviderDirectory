namespace Abm.PD.Core.Api.Settings;

public record DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>
    /// Applies pending EF Core migrations on host startup. Intended for local development only —
    /// keep this false in production so schema changes are a deliberate, reviewed step rather
    /// than something that happens implicitly whenever the service restarts.
    /// </summary>
    public bool RunMigrationsOnStartup { get; init; }
}
