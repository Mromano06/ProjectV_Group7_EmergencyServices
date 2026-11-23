using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Clients;
using MyApplication.Api.Dtos;
using MyApplication.Api.Integration;
using MyApplication.Api.Models;

namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Staff Controller Imp

/// <summary>Proposed local paths for ES-FR-026, with no access to HR storage.</summary>
[ApiController]
[Route("api/emergency/v1/staff")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Viewer")]
public sealed class StaffController(StaffHrClient client) : ControllerBase
{
    /// <summary>
    /// Requests a summary of all current staff
    /// </summary>
    /// <param name="ct">token</param>
    /// <returns></returns>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        try { return Ok(await client.GetCountsAsync(ct)); }
        catch (ExternalServiceException ex) { return Fail(ex); }
    }

    /// <summary>
    ///  Requests for staff hiring, firing or other changes
    /// </summary>
    /// <param name="request">request body</param>
    /// <param name="ct">token</param>
    /// <returns></returns>
    [HttpPost("requests")]
    [Authorize(Roles = "emergency:Admin")]
    public async Task<IActionResult> Submit(StaffChangeRequest request, CancellationToken ct)
    {
        if (request.Action is null ||
            !(request.Action.Equals("Hire", StringComparison.OrdinalIgnoreCase) ||
              request.Action.Equals("Terminate", StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new ErrorResponse("invalid_action", "Action must be Hire or Terminate.", HttpContext.TraceIdentifier));
        if (request.Action.Equals("Terminate", StringComparison.OrdinalIgnoreCase) && !request.StaffId.HasValue)
            return BadRequest(new ErrorResponse("missing_staff_id", "Termination requires StaffId.", HttpContext.TraceIdentifier));
        try { return Ok(await client.SubmitRequestAsync(request, ct)); }
        catch (ExternalServiceException ex) { return Fail(ex); }
    }

    /// <summary>
    ///  Error handling for the staff dto
    /// </summary>
    /// <param name="ex">exception</param>
    /// <returns></returns>
    private ObjectResult Fail(ExternalServiceException ex) => StatusCode(ex.HttpStatus,
        new ErrorResponse("hr_unavailable", ex.Message, Activity.Current?.Id ?? HttpContext.TraceIdentifier));
}
