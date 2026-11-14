using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Clients;
using MyApplication.Api.Data;
using MyApplication.Api.Integration;

namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Incident Road Closure controllers

/// <summary>Proposed incident-scoped endpoint for ES-FR-010.</summary>
[ApiController]
[Route("api/emergency/v1/incidents/{incidentId:guid}/road-closures")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
public sealed class IncidentRoadClosuresController(EmergencyDbContext db, RoadClosureClient maintenance)
    : OperationsControllerBase
{
    // Used to get nearby road closures
    [HttpGet]
    public async Task<IActionResult> Get(Guid incidentId, CancellationToken ct)
    {
        var incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return Error(404, "incident_not_found", "Incident not found.");
        try
        {
            return Ok(await maintenance.GetNearbyAsync(incident.Location.Latitude, incident.Location.Longitude, ct));
        }
        catch (ExternalServiceException ex)
        { return Error(ex.HttpStatus, "maintenance_unavailable", ex.Message); }
    }
}
