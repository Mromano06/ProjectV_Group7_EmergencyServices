using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Dtos;
using MyApplication.Api.Services;

namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Station Controller Imp

// Uses the stations route
[ApiController]
[Route("api/emergency/v1/stations")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
public sealed class StationsController(IStationService service) : OperationsControllerBase
{
    // Request to get an existing station within the system
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await service.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var station = await service.GetAsync(id, ct);
        return station is null ? Error(404, "station_not_found", "Station not found.") : Ok(station);
    }

    // Request to create a new station within the system
    [HttpPost]
    [Authorize(Roles = "emergency:Admin")]
    public async Task<IActionResult> Create(StationSaveRequest req, CancellationToken ct)
    {
        var result = await service.CreateAsync(req, CurrentUserId(), ct);
        if (result.HttpStatus != 201) return FromService(result);
        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }

    // Request to update a new station within the system
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "emergency:Admin")]
    public async Task<IActionResult> Update(Guid id, StationSaveRequest req, CancellationToken ct)
        => FromService(await service.UpdateAsync(id, req, CurrentUserId(), ct));
}
