using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;
using MyApplication.Api.Services;

namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Stats Controller Imp

[ApiController]
[Route("api/emergency/v1/units")]
[Authorize(Roles = "emergency:Dispatcher,emergency:Admin")]
public sealed class UnitsController(IUnitService service) : OperationsControllerBase
{
    // Requests all current units information
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] UnitStatus? status, [FromQuery] UnitType? type,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await service.ListAsync(status, type, page, pageSize, ct));

    // Requests a specific current units information
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var unit = await service.GetAsync(id, ct);
        return unit is null ? Error(404, "unit_not_found", "Unit not found.") : Ok(unit);
    }

    // Request to create a brand new unit
    [HttpPost]
    [Authorize(Roles = "emergency:Admin")]
    public async Task<IActionResult> Create(UnitCreateRequest req, CancellationToken ct)
    {
        var result = await service.CreateAsync(req, CurrentUserId(), ct);
        if (result.HttpStatus != 201) return FromService(result);
        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }

    // Request to update an already existing unit
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "emergency:Admin")]
    public async Task<IActionResult> Update(Guid id, UnitUpdateRequest req, CancellationToken ct)
        => FromService(await service.UpdateAsync(id, req, CurrentUserId(), ct));

    // Request to end/deactivate a unit
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = "emergency:Admin")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
        => FromService(await service.DeactivateAsync(id, CurrentUserId(), ct));
}
