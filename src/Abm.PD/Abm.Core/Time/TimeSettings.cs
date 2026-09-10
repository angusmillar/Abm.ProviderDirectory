using System.ComponentModel.DataAnnotations;

namespace Abm.Core.Time;

public record TimeSettings
{
  public const string SectionName = "Time";

  [Range(typeof(TimeSpan), "00:00", "23:59")]
  public TimeSpan ServiceDefaultTimeZone { get; init; } = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
}
