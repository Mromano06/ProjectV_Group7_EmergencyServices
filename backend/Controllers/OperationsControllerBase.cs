using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Models;
using MyApplication.Api.Services;

namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Operations Controller Imp
// Base for all operations controllers

/// <summary>Shared SH-I03 response helpers for operations endpoints.</summary>
public abstract class OperationsControllerBase : ControllerBase
{
    // Guid for the current user
    protected Guid CurrentUserId()
    {
        var text = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(text, out var result) ? result : Guid.Empty;
    }

    // Error handling for status'
    protected ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new ErrorResponse(code, message,
            Activity.Current?.Id ?? HttpContext.TraceIdentifier));

    // Error handling for the results of each service
    protected IActionResult FromService<T>(ServiceResult<T> result)
    {
        if (result.HttpStatus is >= 200 and < 300)
            return StatusCode(result.HttpStatus, result.Value);
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return StatusCode(result.HttpStatus,
            new ErrorResponse(result.Error ?? "operation_failed", result.Message ?? "Request failed.", traceId));
    }
}