using System.ComponentModel.DataAnnotations;

namespace Abm.PD.Domain.Settings;

public record ServiceDefaultTimeZoneSettings
{
  public const string SectionName = "ServiceDefaultTimeZone";

  [Range(typeof(TimeSpan), "00:00", "23:59")]
  public TimeSpan TimeZoneTimeSpan { get; init; } = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
}
