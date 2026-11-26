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
/// POST /api/transport/v1/road-closures, from Transit's updated Swagger (Nov 26).
/// Transit requires the SH-I06 <see cref="Address"/> object and <see cref="ValidUntil"/>; everything else is optional.
/// ExternalId is our incident ID, so sending again for the same incident updates their record
/// instead of making a new one (they answer 201 created / 200 updated).
/// No external_reference: Transit treats it as a second ID and rejects it when it differs from
/// external_id ("external_id conflicts with external_reference", 400).
/// No end_time either: Transit takes valid-until OR end_time, not both ("provide either valid-until or
/// end_time, not both", 400). valid-until is the required one, so the end time goes there.
/// Note the JSON name "valid-until" has a hyphen, unlike every other (snake_case) field.
/// Transit's Swagger example uses status "scheduled"; we send "active" / "resolved".
/// </summary>
public sealed record TransitRoadClosure(
    [property: JsonPropertyName("external_id")] string ExternalId,
    [property: JsonPropertyName("source_service")] string SourceService,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("address")] SharedAddress Address,
    [property: JsonPropertyName("affected_area")] string AffectedArea,
    [property: JsonPropertyName("affected_segments")] IReadOnlyList<string> AffectedSegments,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("radius_meters")] double RadiusMeters,
    [property: JsonPropertyName("start_time")] string StartTime,
    [property: JsonPropertyName("valid-until")] long ValidUntil)
{
    /// <summary>Who we are in Transit's source_service field.</summary>
    public const string Source = "emergency";

    /// <summary>RFC 3339 in UTC, e.g. 2026-11-25T22:30:00Z (what Transit's API reads).</summary>
    public static string Rfc3339(DateTime utc) =>
        DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Transit's valid-until as a Unix epoch timestamp in seconds. Transit reads any value
    /// &gt;= 1,000,000,000 as an absolute time (smaller values would mean "seconds after start_time").
    /// </summary>
    public static long UnixSeconds(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc)).ToUnixTimeSeconds();
}

/// <summary>
/// What we POST to Transit's /road-closures when the road is open again (their Integration Guide, Nov 26):
/// the same external_id with status "resolved". Only these three fields: Transit rejects unknown fields
/// with 400, and a resolve needs no address or valid-until.
/// </summary>
public sealed record TransitRoadClosureResolution(
    [property: JsonPropertyName("external_id")] string ExternalId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status);

/// <summary>Our reply after Transit accepted the notice. Status is "active" (sent or updated) or "resolved" (cleared).</summary>
public sealed record RoadRestrictionResult(Guid IncidentId, bool SentToTransit, DateTime SentAt, string Status);