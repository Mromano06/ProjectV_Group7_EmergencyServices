using System.ComponentModel.DataAnnotations;
using MyApplication.Api.Enums;

namespace MyApplication.Api.Models;

// "DTOs" = the shapes of JSON that go in and out of the API.
// We use these instead of the database class so callers can't set things like Id or CreatedBy.

/// <summary>Body for POST /weather-warnings (issue a new warning).</summary>
public class CreateWeatherWarningRequest
{
    [Required, MaxLength(100)]
    public string Type { get; set; } = string.Empty;

    [Required]
    public string Severity { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Area { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Optional – leave empty to start right now.</summary>
    public DateTime? StartsAt { get; set; }

    [Required]
    public DateTime EndsAt { get; set; }

    public bool AffectsTransit { get; set; }
}

/// <summary>
/// Body for PUT /weather-warnings/{id} (update or extend an active warning).
/// Same fields as create; to "extend", send a later EndsAt.
/// </summary>
public class UpdateWeatherWarningRequest : CreateWeatherWarningRequest
{
}

/// <summary>What the API sends back for a warning.</summary>
public record WeatherWarningDto(
    Guid Id,
    string Type,
    string Severity,
    string Area,
    string? Description,
    DateTime StartsAt,
    DateTime EndsAt,
    string Status,          // Scheduled / Active / Ended
    string Source,          // Manual / Simulated
    bool AffectsTransit,
    DateTime? LastSentToTransitAt,
    List<Guid> LinkedIncidentIds,
    Guid CreatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public static WeatherWarningDto From(WeatherWarning w, DateTime now) => new(
        w.Id,
        w.Type,
        w.Severity,
        w.Area,
        w.Description,
        w.StartsAt,
        w.EndsAt,
        w.GetStatus(now).ToString(),
        w.Source.ToString(),
        w.AffectsTransit,
        w.LastSentToTransitAt,
        w.IncidentLinks.Select(l => l.IncidentId).ToList(),
        w.CreatedBy,
        w.CreatedAt,
        w.UpdatedAt);
}

/// <summary>A page of results (SH-I05: ?page=&amp;pageSize=, default 25).</summary>
public record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Message sent to Public Transport about a service-affecting warning.</summary>
public record TransitWarningNotice(
    Guid WarningId,
    string Type,
    string Severity,
    string Area,
    DateTime StartsAt,
    DateTime EndsAt,
    string Status);

/// <summary>The one error shape every service uses (SH-I03).</summary>
public record ErrorResponse(string Error, string Message, string TraceId);
