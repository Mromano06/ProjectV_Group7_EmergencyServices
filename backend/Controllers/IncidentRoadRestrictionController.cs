using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Clients;
using MyApplication.Api.Data;
using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;

namespace MyApplication.Api.Controllers;

/// <summary>
/// ES-FR-015 / SH-C13 / PT-FR-019: mark an incident as blocking roads so Public Transport can
/// re-route buses. Transit asked for a one-time notice (no updates or clears).
/// </summary>
[ApiController]
[Route("api/emergency/v1/incidents/{incidentId:guid}/road-restriction")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
public sealed class IncidentRoadRestrictionController(
    EmergencyDbContext db, ITransitWarningClient transit, ILogger<IncidentRoadRestrictionController> logger)
    : OperationsControllerBase
{
    /// <summary>Sends the road-restriction notice for this incident to Public Transport.</summary>
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
        var notice = new TransitIncidentNotice(
            incident.Id, incident.Type.ToString(),
            incident.Location.Latitude, incident.Location.Longitude, incident.Location.Address,
            request.AffectedArea.Trim(), request.RestrictionType.Trim(), endsAt, now);

        if (!await transit.SendIncidentNoticeAsync(notice))
            return Error(502, "transit_unavailable",
                "Public Transport did not accept the notice. Try again; the incident itself is unchanged.");

        logger.LogInformation("Road restriction for incident {IncidentId} sent to Transit by {User}",
            incident.Id, CurrentUserId());
        return Ok(new RoadRestrictionResult(incident.Id, true, now));
    }
}
