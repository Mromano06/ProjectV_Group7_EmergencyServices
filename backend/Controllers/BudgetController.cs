using System.Diagnostics;
using System.Text.Json;
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
    /// Sends an expense statement (equipment, overtime, fuel…) to Budget for our department (EMS).
    /// 200 = approved; 400 = Budget refused it (e.g. over budget) with Budget's reason.
    /// </summary>
    /// <param name="request">amount and description</param>
    /// <param name="ct">token</param>
    [HttpPost("expenses")]
    [Authorize(Roles = "emergency:Admin")]
    public async Task<IActionResult> Expense(EmergencyExpenseRequest request, CancellationToken ct)
    {
        if (decimal.Round(request.Amount, 2) != request.Amount)
            return BadRequest(new ErrorResponse("invalid_amount", "Amount can have at most 2 decimals (cents).", HttpContext.TraceIdentifier));
        var body = new ExpenseRequest(BudgetFinanceClient.DepartmentCode, request.Amount, request.Description.Trim());
        try
        {
            var result = await client.SubmitExpenseAsync(body, ct);
            // Budget may answer 2xx with { approved: false, message } instead of a 400 – still a refusal.
            if (result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty("approved", out var approved) && approved.ValueKind == JsonValueKind.False)
            {
                var reason = result.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString()! : "Budget did not approve the expense.";
                return BadRequest(new ErrorResponse("budget_rejected", reason,
                    Activity.Current?.Id ?? HttpContext.TraceIdentifier));
            }
            return Ok(result);
        }
        catch (ExternalServiceException ex) { return Fail(ex); }
    }

    /// <summary>
    /// Asks Budget for more money (BF-07). Urgent Emergency requests are reviewed first.
    /// </summary>
    /// <param name="request">amount, reason and whether it is urgent</param>
    /// <param name="ct">token</param>
    [HttpPost("funding-requests")]
    [Authorize(Roles = "emergency:Admin")]
    public async Task<IActionResult> RequestFunding(FundingRequest request, CancellationToken ct)
    {
        if (decimal.Round(request.Amount, 2) != request.Amount)
            return BadRequest(new ErrorResponse("invalid_amount", "Amount can have at most 2 decimals (cents).", HttpContext.TraceIdentifier));
        var body = new BudgetFundingRequest(BudgetFinanceClient.DepartmentCode, request.Amount,
            request.Reason.Trim(), request.Urgent);
        try { return Ok(await client.SubmitFundingRequestAsync(body, ct)); }
        catch (ExternalServiceException ex) { return Fail(ex); }
    }

    /// <summary>
    /// Budget said no (4xx) -> "budget_rejected" with Budget's reason;
    /// Budget broken or unreachable (5xx) -> "budget_unavailable".
    /// </summary>
    private ObjectResult Fail(ExternalServiceException ex) => StatusCode(ex.HttpStatus,
        new ErrorResponse(ex.HttpStatus < 500 ? "budget_rejected" : "budget_unavailable", ex.Message,
            Activity.Current?.Id ?? HttpContext.TraceIdentifier));
}
