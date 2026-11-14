using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;
using MyApplication.Api.Services;

namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Incident controllers

/// <summary>Base incidents API; assignment routes live in IncidentAssignmentsController.</summary>
[ApiController]
[Route("api/emergency/v1/incidents")]
[Authorize]
public sealed class IncidentsController(IIncidentService service) : OperationsControllerBase
{
    // Gets the currently assigned status of all existing incidents
    [HttpGet]
    [Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
    public async Task<IActionResult> List([FromQuery] IncidentStatus? status, [FromQuery] IncidentType? type,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await service.ListAsync(status, type, page, pageSize, ct));

    // Gets the currently assigned status of a specific existing incident
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var incident = await service.GetAsync(id, ct);
        return incident is null ? Error(404, "incident_not_found", "Incident not found.") : Ok(incident);
    }

    // Security's service credential claim/role needs confirmation with Registration team.
    // Requests the creation of a brand new incident
    [HttpPost]
    [Authorize(Policy = "EmergencyIncidentCreate")]
    public async Task<IActionResult> Create([FromBody] IncidentCreateRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, CurrentUserId(), ct);
        if (result.HttpStatus != 201) return FromService(result);
        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }

    // Requests the status update of an existing incident
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
    public async Task<IActionResult> Status(Guid id, [FromBody] ChangeIncidentStatusRequest request, CancellationToken ct)
        => FromService(await service.ChangeStatusAsync(id, request, CurrentUserId(), ct));

    // Requests the cretation of a new incident report
    [HttpPost("{id:guid}/report")]
    [Authorize(Roles = "emergency:Admin,emergency:Responder")]
    public async Task<IActionResult> Report(Guid id, [FromBody] FileAfterActionReportRequest request, CancellationToken ct)
        => FromService(await service.FileReportAsync(id, request, CurrentUserId(), ct));
}
