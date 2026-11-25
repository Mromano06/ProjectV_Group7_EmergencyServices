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
    /// <summary>
    /// Requests the current budget information
    /// </summary>
    /// <param name="ct">token</param>
    /// <returns></returns>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try { return Ok(await client.GetAllocationAsync(ct)); }
        catch (ExternalServiceException ex) { return Fail(ex); }
    }

    /// <summary>
    /// Request to create a new expenses report
    /// </summary>
    /// <param name="request">request body</param>
    /// <param name="ct">token</param>
    /// <returns></returns>
    [HttpPost("expenses")]
    [Authorize(Roles = "emergency:Admin")]
    public async Task<IActionResult> Expense(ExpenseRequest request, CancellationToken ct)
    {
        // DepartmentCode, Amount and Description are validated by the [Required]/[Range]
        // attributes on ExpenseRequest (Budget's contract), so no extra checks are needed here.
        try { return Ok(await client.SubmitExpenseAsync(request, ct)); }
        catch (ExternalServiceException ex) { return Fail(ex); }
    }

    ///<summary>Dto error handling </summary> 
    private ObjectResult Fail(ExternalServiceException ex) => StatusCode(ex.HttpStatus,
        new ErrorResponse("budget_unavailable", ex.Message, Activity.Current?.Id ?? HttpContext.TraceIdentifier));
}
