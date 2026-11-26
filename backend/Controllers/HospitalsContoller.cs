using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;
using MyApplication.Api.Services;

namespace MyApplication.Api.Controllers;

// Matthew Romano - Nov 14th, 2026 - Hospital controllers

/// <summary>
/// Provides existing hospital information to the emergency
/// services frontend, and Admin hospital maintenance (ES-FR-012/013).
/// </summary>
[ApiController]
[Route("api/emergency/v1/hospitals")]
[Authorize(Roles = "emergency:Admin,emergency:Dispatcher,emergency:Responder")]
public class HospitalsController : ControllerBase
{
    private readonly IHospitalService _service;

    public HospitalsController(IHospitalService service)
    {
        _service = service;
    }

    /// <summary>
    /// GET /hospitals?near=latitude,longitude&amp;open=true
    /// Returns hospitals, optionally restricted to Open ERs and sorted by distance.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetHospitals(
        [FromQuery] string? near, [FromQuery] bool open, CancellationToken ct)
    {
        double? latitude = null;
        double? longitude = null;

        if (near is not null)
        {
            if (!TryParseNear(near, out var lat, out var lon))
                return Error(400, "invalid_near", "near must be latitude,longitude with valid coordinates.");

            latitude = lat;
            longitude = lon;
        }

        var hospitals = await _service.SearchAsync(latitude, longitude, open, ct);
        return Ok(hospitals.Select(h => ToDto(h.Hospital, h.DistanceKm)).ToList());
    }

    /// <summary>
    /// GET /hospitals/summary
    /// Returns total hospital count and ER bed availability.
    /// Kept for compatibility with the existing frontend.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary()
    {
        var hospitals = await _service.GetAllAsync();
        return Ok(new
        {
            HospitalCount = hospitals.Count,
            AvailableBeds = hospitals.Sum(h => h.AvailableBeds),
            OpenHospitals = hospitals.Count(h => h.ErStatus == HospitalStatus.Open),
            DivertingHospitals = hospitals.Count(h => h.ErStatus == HospitalStatus.Diverting),
            ClosedHospitals = hospitals.Count(h => h.ErStatus == HospitalStatus.Closed)
        });
    }

    /// <summary>
    /// GET /hospitals/{id}
    /// Returns information for one hospital, including the last change time.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var hospital = await _service.GetByIdAsync(id);
        if (hospital is null)
            return Error(404, "hospital_not_found", $"No hospital with id {id}.");

        return Ok(ToDto(hospital));
    }

    /// <summary>POST /hospitals - Admin registers a hospital.</summary>
    [HttpPost]
    [Authorize(Roles = "emergency:Admin")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] HospitalSaveRequest request, CancellationToken ct)
    {
        if (!TryBuildHospital(request, out var hospital, out var problem))
            return Error(400, "invalid_hospital", problem);

        var saved = await _service.CreateAsync(hospital!, ct);
        return CreatedAtAction(nameof(GetById), new { id = saved.Id }, ToDto(saved));
    }

    /// <summary>PUT /hospitals/{id} - Admin updates a hospital's full record.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "emergency:Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] HospitalSaveRequest request, CancellationToken ct)
    {
        if (!TryBuildHospital(request, out var hospital, out var problem))
            return Error(400, "invalid_hospital", problem);

        var saved = await _service.UpdateAsync(id, hospital!, ct);
        return saved is null
            ? Error(404, "hospital_not_found", "Hospital not found.")
            : Ok(ToDto(saved));
    }

    /// <summary>PATCH /hospitals/{id}/er - Admin updates ER status and bed count.</summary>
    [HttpPatch("{id:guid}/er")]
    [Authorize(Roles = "emergency:Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateEr(
        Guid id, [FromBody] HospitalErUpdateRequest request, CancellationToken ct)
    {
        if (request?.ErStatus is null || !Enum.IsDefined(request.ErStatus.Value) ||
            request.AvailableBeds is null or < 0)
            return Error(400, "invalid_er_update", "Provide a valid ER status and a non-negative availableBeds value.");

        var saved = await _service.UpdateErAsync(
            id, request.ErStatus.Value, request.AvailableBeds.Value, ct);

        return saved is null
            ? Error(404, "hospital_not_found", "Hospital not found.")
            : Ok(ToDto(saved));
    }

    // ---------- Helpers ----------
    private static HospitalDto ToDto(Hospital hospital, double? distanceKm = null)
    {
        var updatedAt = hospital.UpdatedAt ?? hospital.CreatedAt;
        return new HospitalDto(
            hospital.Id,
            hospital.Name,
            new HospitalLocationDto(hospital.Location.Latitude,
                hospital.Location.Longitude, hospital.Location.Address),
            hospital.ErStatus.ToString(),
            hospital.AvailableBeds,
            DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc),
            distanceKm.HasValue ? Math.Round(distanceKm.Value, 2) : null);
    }

    private static bool TryBuildHospital(
        HospitalSaveRequest? request, out Hospital? hospital, out string problem)
    {
        hospital = null;
        problem = string.Empty;

        if (request is null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 200)
        {
            problem = "Hospital name is required (maximum 200 characters).";
            return false;
        }

        var location = request.Location;
        if (location?.Latitude is not double latitude ||
            location.Longitude is not double longitude ||
            !double.IsFinite(latitude) || latitude < -90 || latitude > 90 ||
            !double.IsFinite(longitude) || longitude < -180 || longitude > 180 ||
            location.Address?.Length > 500)
        {
            problem = "Provide valid location coordinates and an address of at most 500 characters.";
            return false;
        }

        if (request.ErStatus is null || !Enum.IsDefined(request.ErStatus.Value) ||
            request.AvailableBeds is null or < 0)
        {
            problem = "Provide erStatus (Open/Diverting/Closed) and non-negative availableBeds.";
            return false;
        }

        hospital = new Hospital
        {
            Name = request.Name.Trim(),
            Location = new Location
            {
                Latitude = latitude,
                Longitude = longitude,
                Address = location.Address?.Trim()
            },
            ErStatus = request.ErStatus.Value,
            AvailableBeds = request.AvailableBeds.Value
        };
        return true;
    }

    private static bool TryParseNear(string near, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        var parts = near.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
               double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) &&
               double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude) &&
               double.IsFinite(latitude) && latitude >= -90 && latitude <= 90 &&
               double.IsFinite(longitude) && longitude >= -180 && longitude <= 180;
    }

    private ObjectResult Error(int status, string code, string message)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return StatusCode(status, new ErrorResponse(code, message, traceId));
    }
}
