using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Services;
namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Incident Exports controllers

/// <summary>ES-FR-027; separate controller to avoid replacing existing IncidentsController.</summary>
[ApiController]
[Route("api/emergency/v1/incidents")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher")]
public sealed class IncidentExportController(IStatsService service) : OperationsControllerBase
{
    /// <summary>
    /// Handles exporting of emergency incidents into a CSV format
    /// </summary>
    /// <param name="from">starting timestamp</param>
    /// <param name="to">ending timestamp</param>
    /// <param name="ct">token</param>
    /// <returns></returns>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken ct)
    {
        if (from == default || to == default)
            return BadRequest(new { error = "date_range_required", message = "Supply from and to in UTC.", traceId = HttpContext.TraceIdentifier });
        var result = await service.ExportCsvAsync(from.UtcDateTime, to.UtcDateTime, ct);
        if (result.HttpStatus != 200) return FromService(result);
        return File(Encoding.UTF8.GetBytes(result.Value!), "text/csv", "emergency-incidents.csv");
    }
}
