using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Models;
using MyApplication.Api.Services;

namespace MyApplication.Api.Controllers;

/// <summary>
/// Weather and public warnings (ES-FR-021, ES-FR-022, SH-C13).
/// Route follows SH-I01: /api/emergency/v1/weather-warnings
/// </summary>
[ApiController]
[Route("api/emergency/v1/weather-warnings")]
[Authorize] // everything below needs a logged-in user or service (SH-S03)
public class WeatherWarningsController : ControllerBase
{
    // Role names as they appear in the JWT (SH-S02).
    private const string Admin = "emergency:Admin";
    private const string AdminOrDispatcher = "emergency:Admin,emergency:Dispatcher";

    private readonly IWeatherWarningService _service;

    public WeatherWarningsController(IWeatherWarningService service)
    {
        _service = service;
    }

    /// <summary>
    /// GET ?active=true  -> warnings in effect right now (app banner, other services, incident board).
    /// GET (no active)   -> current AND past warnings, newest first, paged.
    /// Open to ANY logged-in user or service (ES-FR-022).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWarnings([FromQuery] bool active = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var now = DateTime.UtcNow;

        if (active)
        {
            var current = await _service.GetActiveAsync();
            return Ok(current.Select(w => WeatherWarningDto.From(w, now)).ToList());
        }

        var history = await _service.GetHistoryAsync(page, pageSize);
        return Ok(new PagedResult<WeatherWarningDto>(
            history.Items.Select(w => WeatherWarningDto.From(w, now)).ToList(),
            history.Page,
            history.PageSize,
            history.TotalCount));
    }

    /// <summary>GET /{id} -> one warning, including linked incident IDs.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var warning = await _service.GetByIdAsync(id);
        if (warning == null)
        {
            return Error(StatusCodes.Status404NotFound, "warning_not_found", $"No warning with id {id}.");
        }
        return Ok(WeatherWarningDto.From(warning, DateTime.UtcNow));
    }

    /// <summary>POST -> Admin issues a new warning. Returns 201 with the new warning.</summary>
    [HttpPost]
    [Authorize(Roles = Admin)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateWeatherWarningRequest request)
    {
        var result = await _service.CreateAsync(request, CurrentUserId());
        if (result.Outcome != WarningOutcome.Ok) return FromResult(result);

        var dto = WeatherWarningDto.From(result.Warning!, DateTime.UtcNow);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>PUT /{id} -> Admin updates or extends a warning that hasn't ended.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWeatherWarningRequest request)
    {
        var result = await _service.UpdateAsync(id, request, CurrentUserId());
        return FromResult(result);
    }

    /// <summary>POST /{id}/expire -> Admin ends a warning early. (No DELETE: we keep history.)</summary>
    [HttpPost("{id:guid}/expire")]
    [Authorize(Roles = Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Expire(Guid id)
    {
        var result = await _service.ExpireAsync(id, CurrentUserId());
        return FromResult(result);
    }

    /// <summary>
    /// POST /{id}/notify-transit -> Admin sends (or re-sends) this warning to Public Transport.
    /// Returns 200 if Transit accepted it, 502 if Transit couldn't be reached.
    /// </summary>
    [HttpPost("{id:guid}/notify-transit")]
    [Authorize(Roles = Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> NotifyTransit(Guid id)
    {
        var (result, sent) = await _service.NotifyTransitAsync(id);
        if (result.Outcome != WarningOutcome.Ok) return FromResult(result);

        if (!sent)
        {
            return Error(StatusCodes.Status502BadGateway, "transit_unavailable",
                "Public Transport could not be reached. The warning is saved; try again later.");
        }
        return Ok(WeatherWarningDto.From(result.Warning!, DateTime.UtcNow));
    }

    /// <summary>
    /// POST /simulate -> Admin makes one random fake warning right now.
    /// Handy during a demo so you don't have to wait for the background simulator.
    /// </summary>
    [HttpPost("simulate")]
    [Authorize(Roles = Admin)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Simulate()
    {
        var result = await SimulatedWarningGenerator.CreateRandomWarningAsync(_service);
        if (result.Outcome != WarningOutcome.Ok) return FromResult(result);

        var dto = WeatherWarningDto.From(result.Warning!, DateTime.UtcNow);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>POST /{id}/incidents -> link an incident to this warning. Body: { "incidentId": "..." }</summary>
    [HttpPost("{id:guid}/incidents")]
    [Authorize(Roles = AdminOrDispatcher)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LinkIncident(Guid id, [FromBody] LinkIncidentRequest request)
    {
        var result = await _service.LinkIncidentAsync(id, request.IncidentId, CurrentUserId());
        return FromResult(result);
    }

    // ---------- Helpers ----------

    /// <summary>Turns a service result into the right HTTP response.</summary>
    private IActionResult FromResult(WarningResult result) => result.Outcome switch
    {
        WarningOutcome.Ok => Ok(WeatherWarningDto.From(result.Warning!, DateTime.UtcNow)),
        WarningOutcome.NotFound => Error(StatusCodes.Status404NotFound, result.ErrorCode, result.Message),
        WarningOutcome.Conflict => Error(StatusCodes.Status409Conflict, result.ErrorCode, result.Message),
        _ => Error(StatusCodes.Status400BadRequest, result.ErrorCode, result.Message)
    };

    /// <summary>Builds the shared error shape { error, message, traceId } (SH-I03).</summary>
    private ObjectResult Error(int status, string code, string message)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return StatusCode(status, new ErrorResponse(code, message, traceId));
    }

    /// <summary>
    /// The logged-in user's ID from the JWT (SH-S02). Falls back to Guid.Empty
    /// if the token's ID isn't a GUID (e.g. test tokens).
    /// </summary>
    private Guid CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}

/// <summary>Body for POST /weather-warnings/{id}/incidents.</summary>
public class LinkIncidentRequest
{
    public Guid IncidentId { get; set; }
}
