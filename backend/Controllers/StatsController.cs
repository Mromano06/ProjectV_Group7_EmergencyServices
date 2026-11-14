using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Services;
namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Stats Controller Imp

[ApiController]
[Route("api/emergency/v1/stats")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder,emergency:Viewer")]
public sealed class StatsController(IStatsService service) : OperationsControllerBase
{
    // Gets the stats from one point of time to another
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken ct)
    {
        if (from == default || to == default) return BadRequest(new { error = "date_range_required", message = "Supply from and to in UTC.", traceId = HttpContext.TraceIdentifier });
        return FromService(await service.GetStatsAsync(from.UtcDateTime, to.UtcDateTime, ct));
    }
}
