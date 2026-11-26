using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;
using MyApplication.Api.Services;

namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Incident Assignments controllers

/// <summary> incident routes for /incidents/{id}/assignments.</summary>
[ApiController]
[Route("api/emergency/v1/incidents/{incidentId:guid}")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher")]
public sealed class IncidentAssignmentsController(IDispatchService service) : OperationsControllerBase
{
    /// <summary> Request to get all incident assignments</summary>
    [HttpGet("assignments")]
    public async Task<IActionResult> List(Guid incidentId, CancellationToken ct)
        => FromService(await service.ListAsync(incidentId, ct));

    /// <summary>
    /// Request to create a new assignment for a incident
    /// </summary>
    /// <param name="incidentId">id of relevant incident</param>
    /// <param name="request">request body</param>
    /// <param name="ct">token</param>
    /// <returns></returns>
    [HttpPost("assignments")]
    public async Task<IActionResult> Assign(Guid incidentId, AssignUnitsRequest request, CancellationToken ct)
        => FromService(await service.AssignAsync(incidentId, request, CurrentUserId(), ct));

    // Below routes may be redundant/unessecary
    // TODO: Implment these featues fully or delete this below

    /// <summary>
    /// Proposed additional route: assignments must be released without deleting audit history.
    /// </summary>
    /// <param name="incidentId">id of relevant incident</param>
    /// <param name="assignmentId">id of assignment</param>
    /// <param name="ct">token</param>
    /// <returns></returns>
    [HttpPost("assignments/{assignmentId:guid}/release")]
    public async Task<IActionResult> Release(Guid incidentId, Guid assignmentId, CancellationToken ct)
        => FromService(await service.ReleaseAsync(incidentId, assignmentId, CurrentUserId(), ct));

    /// <summary>
    /// Proposed route for ES-FR-009 (not explicitly specified in EndPoints tab).
    /// </summary>
    /// <param name="incidentId">id or relevant incident</param>
    /// <param name="type">unit type needed</param>
    /// <param name="ct">token</param>
    /// <returns></returns>
    [HttpGet("suggested-units")]
    public async Task<IActionResult> Suggested(Guid incidentId, [FromQuery] UnitType? type, CancellationToken ct)
    {
        if (!type.HasValue) return Error(400, "missing_type", "A unit type is required.");
        return FromService(await service.SuggestAsync(incidentId, type.Value, ct));
    }
}
