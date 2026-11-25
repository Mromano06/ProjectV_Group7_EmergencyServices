using System.ComponentModel.DataAnnotations;

namespace MyApplication.Api.Dtos;

/// <summary>
/// Body of POST /incidents/{id}/road-restriction (ES-FR-015): a dispatcher or responder
/// marks an incident as blocking roads, and we tell Public Transport.
/// </summary>
/// <param name="AffectedArea">what is blocked, e.g. "King St between Ainslie and Water"</param>
/// <param name="RestrictionType">e.g. "Full closure", "Lane closure", "Detour"</param>
/// <param name="EstimatedEndAt">when the road should reopen (UTC, must be in the future)</param>
public sealed record RoadRestrictionRequest(
    [Required, MinLength(1), MaxLength(300)] string AffectedArea,
    [Required, MinLength(1), MaxLength(50)] string RestrictionType,
    [Required] DateTime? EstimatedEndAt);

/// <summary>
/// What we POST to Public Transport (SH-C13, PT-FR-019):
/// incident ID, location, affected area, restriction type and estimated end time.
/// Transit asked for notices only – no updates or clears.
/// </summary>
public sealed record TransitIncidentNotice(
    Guid IncidentId,
    string IncidentType,
    double Latitude,
    double Longitude,
    string? Address,
    string AffectedArea,
    string RestrictionType,
    DateTime EstimatedEndAt,
    DateTime SentAt);

/// <summary>Our reply after Transit accepted the notice.</summary>
public sealed record RoadRestrictionResult(Guid IncidentId, bool SentToTransit, DateTime SentAt);
