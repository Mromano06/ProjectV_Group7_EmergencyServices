using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Dtos;
using MyApplication.Api.Models;
using MyApplication.Api.Services;

namespace MyApplication.Api.Controllers;

/// <summary>ES-FR-014: patient transports without diagnosis or medical history fields.</summary>
[ApiController]
[Route("api/emergency/v1/incidents/{incidentId:guid}/patient-transports")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
public sealed class PatientTransportsController : ControllerBase
{
    private readonly IPatientTransportService _service;

    public PatientTransportsController(IPatientTransportService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid incidentId, CancellationToken ct)
    {
        var items = await _service.ListAsync(incidentId, ct);
        return items is null
            ? Error(404, "incident_not_found", "Incident not found.")
            : Ok(items);
    }

    [HttpGet("{transportId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid incidentId, Guid transportId, CancellationToken ct)
    {
        var record = await _service.GetByIdAsync(incidentId, transportId, ct);
        return record is null
            ? Error(404, "transport_not_found", "Transport not found.")
            : Ok(record);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid incidentId, [FromBody] CreatePatientTransportRequest request, CancellationToken ct)
    {
        if (request is null || request.HospitalId == Guid.Empty ||
            request.TriageLevel is null || !Enum.IsDefined(request.TriageLevel.Value) ||
            request.ResidentId == Guid.Empty)
            return Error(400, "invalid_transport", "Provide valid hospitalId, triageLevel and optional residentId.");

        try
        {
            var record = await _service.CreateAsync(incidentId, request, ct);
            return CreatedAtAction(nameof(GetById),
                new { incidentId, transportId = record.Id }, record);
        }
        catch (KeyNotFoundException)
        {
            return Error(404, "incident_not_found", "Incident not found.");
        }
        catch (ArgumentException ex)
        {
            return Error(400, "invalid_transport", ex.Message);
        }
    }

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status,
            new ErrorResponse(code, message, Activity.Current?.Id ?? HttpContext.TraceIdentifier));
}
