using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Clients;
using MyApplication.Api.Data;
using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Controllers;

/// <summary>
/// ES-FR-015 / SH-C13 / PT-FR-019: tell Public Transport about incidents that block roads, so they can
/// re-route buses. Sent to Transit's POST /api/transport/v1/integrations/emergency/incidents (their Swagger,
/// Nov 25) with our incident ID as external_id, so:
///   POST   = send the restriction, or update it (same incident ID -> Transit updates its record)
///   DELETE = clear it (same incident ID, status "resolved", ends now)
/// </summary>
[ApiController]
[Route("api/emergency/v1/incidents/{incidentId:guid}/road-restriction")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
public sealed class IncidentRoadRestrictionController(
    EmergencyDbContext db, ITransitWarningClient transit, IConfiguration config,
    ILogger<IncidentRoadRestrictionController> logger)
    : OperationsControllerBase
{
    public const string ActiveStatus = "active";
    public const string ClearedStatus = "resolved";

    /// <summary>Sends (or updates) this incident's road restriction to Public Transport.</summary>
    /// <param name="incidentId">the incident blocking the road</param>
    /// <param name="request">affected area, restriction type and estimated end time</param>
    /// <param name="ct">token</param>
    [HttpPost]
    public async Task<IActionResult> Send(Guid incidentId, RoadRestrictionRequest request, CancellationToken ct)
    {
        var endsAt = request.EstimatedEndAt!.Value.ToUniversalTime();
        if (endsAt <= DateTime.UtcNow)
            return Error(400, "ends_in_past", "EstimatedEndAt must be in the future.");

        var incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return Error(404, "incident_not_found", "Incident not found.");
        if (incident.Status is IncidentStatus.Resolved or IncidentStatus.Closed)
            return Error(409, "incident_inactive", "The incident is resolved or closed; there is no restriction to report.");

        var now = DateTime.UtcNow;
        var notice = Build(incident, request.AffectedArea.Trim(), request.RestrictionType.Trim(), ActiveStatus, now, endsAt);

        if (!await transit.SendEmergencyIncidentAsync(notice))
            return Error(502, "transit_unavailable",
                "Public Transport did not accept the road restriction. Try again; the incident itself is unchanged.");

        logger.LogInformation("Road restriction for incident {IncidentId} sent to Transit by {User}", incident.Id, CurrentUserId());
        return Ok(new RoadRestrictionResult(incident.Id, true, now, ActiveStatus));
    }

    /// <summary>Clears this incident's road restriction at Public Transport (the road is open again).</summary>
    /// <param name="incidentId">the incident that was blocking the road</param>
    /// <param name="ct">token</param>
    [HttpDelete]
    public async Task<IActionResult> Clear(Guid incidentId, CancellationToken ct)
    {
        var incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return Error(404, "incident_not_found", "Incident not found.");

        var now = DateTime.UtcNow;
        var notice = Build(incident, incident.Location.Address ?? "Incident location", "Restriction cleared",
            ClearedStatus, now, now);

        if (!await transit.SendEmergencyIncidentAsync(notice))
            return Error(502, "transit_unavailable",
                "Public Transport did not accept the update. Try again; the incident itself is unchanged.");

        logger.LogInformation("Road restriction for incident {IncidentId} cleared at Transit by {User}", incident.Id, CurrentUserId());
        return Ok(new RoadRestrictionResult(incident.Id, true, now, ClearedStatus));
    }

    /// <summary>Our incident in Transit's EmergencyIncidentInput shape.</summary>
    private TransitEmergencyIncident Build(Incident incident, string affectedArea, string restrictionType,
        string status, DateTime startUtc, DateTime endUtc)
    {
        // The street goes in affected_segments so Transit (and other teams reading their closures) can match it.
        var street = SharedAddress.Parse(incident.Location.Address).StreetName;
        var radius = config.GetValue<double?>("Services:TransitIncidentRadiusMeters") ?? 250;

        return new TransitEmergencyIncident(
            ExternalId: incident.Id.ToString(),
            ExternalReference: "EMS-" + incident.Id.ToString("N")[..8].ToUpperInvariant(),
            Title: $"{incident.Type} emergency – {restrictionType}",
            Description: $"{restrictionType}: {affectedArea}. Reported by Emergency Services.",
            Type: "road_closure",
            IncidentType: incident.Type.ToString().ToLowerInvariant(),
            Severity: incident.Severity.ToString().ToLowerInvariant(),
            Status: status,
            AffectedArea: affectedArea,
            AffectedSegments: string.IsNullOrWhiteSpace(street) || street == "Unknown location" ? [] : [street],
            Latitude: incident.Location.Latitude,
            Longitude: incident.Location.Longitude,
            RadiusMeters: radius,
            StartTime: TransitEmergencyIncident.Rfc3339(startUtc),
            EndTime: TransitEmergencyIncident.Rfc3339(endUtc));
    }
}
