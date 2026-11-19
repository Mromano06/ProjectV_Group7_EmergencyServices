using MyApplication.Api.Enums;

namespace MyApplication.Api.Models;

/// <summary>
/// A weather or public warning issued for the city (Data_Model: WeatherWarning, ES-FR-021).
/// All times are UTC (SH-I04).
/// </summary>
public class WeatherWarning : AuditableEntity
{
    /// <summary>What kind of warning, e.g. "Snowfall", "Freezing Rain", "Boil Water Advisory".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>One of WeatherWarning.AllowedSeverities (Statement, Advisory, Watch, Warning).</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Part of the city it covers, e.g. "City-wide" or "North end".</summary>
    public string Area { get; set; } = string.Empty;

    /// <summary>Optional longer text shown when someone opens the warning.</summary>
    public string? Description { get; set; }

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    /// <summary>Manual (Admin) or Simulated (random demo warning).</summary>
    public WarningSource Source { get; set; } = WarningSource.Manual;

    /// <summary>
    /// True if the warning affects buses/roads, so Public Transport should be told.
    /// </summary>
    public bool AffectsTransit { get; set; }

    /// <summary>Last time we successfully told Public Transport about this warning.</summary>
    public DateTime? LastSentToTransitAt { get; set; }

    /// <summary>Incidents that Dispatch has linked to this warning.</summary>
    public List<WarningIncidentLink> IncidentLinks { get; set; } = new();

    /// <summary>Severity levels, lowest to highest (same names Environment Canada uses).</summary>
    public static readonly string[] AllowedSeverities = { "Statement", "Advisory", "Watch", "Warning" };

    /// <summary>Class diagram method: is this warning in effect at the given time?</summary>
    public bool IsActive(DateTime now) => StartsAt <= now && now < EndsAt;

    /// <summary>Works out Scheduled / Active / Ended from the times.</summary>
    public WarningStatus GetStatus(DateTime now)
    {
        if (now < StartsAt) return WarningStatus.Scheduled;
        if (now < EndsAt) return WarningStatus.Active;
        return WarningStatus.Ended;
    }

    /// <summary>
    /// Class diagram method: end the warning early by moving EndsAt to now.
    /// We never delete warnings, so past warnings can still be viewed.
    /// </summary>
    public void Expire(DateTime now)
    {
        if (EndsAt > now)
        {
            EndsAt = now;
        }
    }
}
