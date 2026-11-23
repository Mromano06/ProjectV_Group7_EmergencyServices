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
    /// <summary> Gets the currently assigned status of all existing incidents </summary>
    /// <param name="IncidentStatus">filter for a particular incident status </param>
    /// <param name="IncidentType"> filter for a particular incident type </param>
    [HttpGet]
    [Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
    public async Task<IActionResult> List([FromQuery] IncidentStatus? status, [FromQuery] IncidentType? type,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await service.ListAsync(status, type, page, pageSize, ct));

    /// <summary>Gets the currently assigned status of a specific existing incident </summary>
    /// <param name="id">id of the incident </param>
    /// <param name="ct"> token </param>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var incident = await service.GetAsync(id, ct);
        return incident is null ? Error(404, "incident_not_found", "Incident not found.") : Ok(incident);
    }

    // Security's service credential claim/role needs confirmation with Registration team.

    ///<summary> Requests the creation of a brand new incident </summary>
    ///<param name="request"> request body </param>
    ///<param name="ct"> token </param>
    [HttpPost]
    [Authorize(Policy = "EmergencyIncidentCreate")]
    public async Task<IActionResult> Create([FromBody] IncidentCreateRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, CurrentUserId(), ct);
        if (result.HttpStatus != 201) return FromService(result);
        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }

    ///<summary>Requests the status update of an existing incident</summary>
    ///<param name="id"> id of existing incident </param>
    ///<param name="request"> request body </param>
    ///<param name="ct"> token </param>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
    public async Task<IActionResult> Status(Guid id, [FromBody] ChangeIncidentStatusRequest request, CancellationToken ct)
        => FromService(await service.ChangeStatusAsync(id, request, CurrentUserId(), ct));

    ///<summary> Requests the creation of a new incident report</summary>
    ///<param name="id"> id of relevant incident </param>
    ///<param name="request"> request </param>
    ///<param name="ct" token </param>
    [HttpPost("{id:guid}/report")]
    [Authorize(Roles = "emergency:Admin,emergency:Responder")]
    public async Task<IActionResult> Report(Guid id, [FromBody] FileAfterActionReportRequest request, CancellationToken ct)
        => FromService(await service.FileReportAsync(id, request, CurrentUserId(), ct));
}
