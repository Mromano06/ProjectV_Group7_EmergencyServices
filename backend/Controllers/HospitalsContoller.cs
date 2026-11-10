
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Models;
using MyApplication.Api.Services;

namespace MyApplication.Api.Controllers;

/// <summary>
/// Provides existing hospital information to the emergency
/// services frontend.
/// </summary>
[ApiController]
[Route("api/emergency/v1/hospitals")]
[Authorize]
public class HospitalsController : ControllerBase
{
    private readonly IHospitalService _service;

    public HospitalsController(IHospitalService service)
    {
        _service = service;
    }

    /// <summary>
    /// GET /hospitals
    /// Returns all hospitals and their current capacity.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHospitals()
    {
        var hospitals = await _service.GetAllAsync();

        return Ok(hospitals.Select(ToDto).ToList());
    }

    /// <summary>
    /// GET /hospitals/summary
    /// Returns total hospital count and room availability.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary()
    {
        var hospitals = await _service.GetAllAsync();

        return Ok(new
        {
            HospitalCount = hospitals.Count,
            TotalRooms = hospitals.Sum(h => h.TotalRooms),
            OccupiedRooms = hospitals.Sum(h => h.OccupiedRooms),
            AvailableRooms = hospitals.Sum(h => h.AvailableRooms)
        });
    }

    /// <summary>
    /// GET /hospitals/{id}
    /// Returns information for one hospital.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var hospital = await _service.GetByIdAsync(id);

        if (hospital == null)
        {
            return Error(
                StatusCodes.Status404NotFound,
                "hospital_not_found",
                $"No hospital with id {id}.");
        }

        return Ok(ToDto(hospital));
    }

    // ---------- Helpers ----------
    private static HospitalDto ToDto(Hospital hospital)
    {
        return new HospitalDto(
            hospital.Id,
            hospital.Name,
            hospital.Location,
            hospital.TotalRooms,
            hospital.OccupiedRooms,
            hospital.AvailableRooms,
            hospital.Status.ToString()
        );
    }

    private ObjectResult Error(
        int status,
        string code,
        string message)
    {
        var traceId = Activity.Current?.Id
                      ?? HttpContext.TraceIdentifier;

        return StatusCode(
            status,
            new ErrorResponse(code, message, traceId));
    }
}

/// <summary>
/// Hospital information returned to API clients.
/// </summary>
public record HospitalDto(
    Guid Id,
    string Name,
    string Location,
    int TotalRooms,
    int OccupiedRooms,
    int AvailableRooms,
    string Status
);
