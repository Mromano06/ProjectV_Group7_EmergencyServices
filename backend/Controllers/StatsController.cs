using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Services;
namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Stats Controller Imp

/// <summary>
/// controller / route for statitics such as incident counts, response times, deaths, etc.
/// </summary>
[ApiController]
[Route("api/emergency/v1/stats")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder,emergency:Viewer")]
public sealed class StatsController(IStatsService service) : OperationsControllerBase
{
    /// <summary>
    /// Gets the stats from one point of time to another
    /// </summary>
    /// <param name="from">starting timestamp</param>
    /// <param name="to">ending timestamp</param>
    /// <param name="ct">token</param>
    /// <returns></returns>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken ct)
    {
        if (from == default || to == default) return BadRequest(new { error = "date_range_required", message = "Supply from and to in UTC.", traceId = HttpContext.TraceIdentifier });
        return FromService(await service.GetStatsAsync(from.UtcDateTime, to.UtcDateTime, ct));
    }
}
