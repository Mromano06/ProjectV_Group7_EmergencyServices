using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;

namespace MyApplication.Api.Dtos;

/// <summary>
/// Body of POST /incidents/{id}/road-restriction (ES-FR-015): a dispatcher or responder
/// marks an incident as blocking roads (or changes the details), and we tell Public Transport.
/// </summary>
/// <param name="AffectedArea">what is blocked, e.g. "King St between Ainslie and Water"</param>
/// <param name="RestrictionType">e.g. "Full closure", "Lane closure", "Detour"</param>
/// <param name="EstimatedEndAt">when the road should reopen (UTC, must be in the future)</param>
public sealed record RoadRestrictionRequest(
    [Required, MinLength(1), MaxLength(300)] string AffectedArea,
    [Required, MinLength(1), MaxLength(50)] string RestrictionType,
    [Required] DateTime? EstimatedEndAt);

/// <summary>
/// What we POST to Public Transport (ES-FR-015, SH-C13, PT-FR-019):
/// POST /api/transport/v1/integrations/emergency/incidents, body disruptions.EmergencyIncidentInput
/// from Transit's Swagger (Nov 25). Their API uses snake_case names and RFC 3339 time strings.
/// ExternalId is our incident ID, so sending again for the same incident updates their record
/// instead of making a new one (they answer 201 created / 200 updated).
/// Transit's Swagger lists no allowed values for type, incident_type, severity or status yet;
/// ours are "road_closure", our incident type/severity in lower case, and "active" / "resolved".
/// </summary>
public sealed record TransitEmergencyIncident(
    [property: JsonPropertyName("external_id")] string ExternalId,
    [property: JsonPropertyName("external_reference")] string ExternalReference,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("incident_type")] string IncidentType,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("affected_area")] string AffectedArea,
    [property: JsonPropertyName("affected_segments")] IReadOnlyList<string> AffectedSegments,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("radius_meters")] double RadiusMeters,
    [property: JsonPropertyName("start_time")] string StartTime,
    [property: JsonPropertyName("end_time")] string EndTime)
{
    /// <summary>RFC 3339 in UTC, e.g. 2026-11-25T22:30:00Z (what Transit's API reads).</summary>
    public static string Rfc3339(DateTime utc) =>
        DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>Our reply after Transit accepted the notice. Status is "active" (sent or updated) or "resolved" (cleared).</summary>
public sealed record RoadRestrictionResult(Guid IncidentId, bool SentToTransit, DateTime SentAt, string Status);
