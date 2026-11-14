using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Clients;
using MyApplication.Api.Dtos;
using MyApplication.Api.Integration;
using MyApplication.Api.Models;

namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Budget controllers

/// <summary>Proposed local REST paths for ES-FR-025; upstream routes follow SH-C08/09.</summary>
[ApiController]
[Route("api/emergency/v1/budget")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder,emergency:Viewer")]
public sealed class BudgetController(BudgetFinanceClient client) : ControllerBase
{
    // Requests the current budget information
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try { return Ok(await client.GetAllocationAsync(ct)); }
        catch (ExternalServiceException ex) { return Fail(ex); }
    }

    // Request to create a new expenses report
    [HttpPost("expenses")]
    [Authorize(Roles = "emergency:Admin")]
    public async Task<IActionResult> Expense(ExpenseRequest request, CancellationToken ct)
    {
        if (request.IncurredAt == default) return BadRequest(new ErrorResponse("invalid_incurred_at", "IncurredAt is required.", HttpContext.TraceIdentifier));
        try { return Ok(await client.SubmitExpenseAsync(request, ct)); }
        catch (ExternalServiceException ex) { return Fail(ex); }
    }

    // Dto error handling
    private ObjectResult Fail(ExternalServiceException ex) => StatusCode(ex.HttpStatus,
        new ErrorResponse("budget_unavailable", ex.Message, Activity.Current?.Id ?? HttpContext.TraceIdentifier));
}
