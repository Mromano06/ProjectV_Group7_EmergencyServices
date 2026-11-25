
using MyApplication.Api.Enums;

// Matthew Romano - 25th Nov, 2026 - Hospital Dto

namespace MyApplication.Api.Dtos;

// Hospital API request/response models (ES-FR-012, ES-FR-013).
// These are separate from the EF Core Hospital entity.

public sealed class HospitalLocationRequest
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Address { get; set; }
}

public sealed class HospitalSaveRequest
{
    public string? Name { get; set; }
    public HospitalLocationRequest? Location { get; set; }
    public HospitalStatus? ErStatus { get; set; }
    public int? AvailableBeds { get; set; }
}

/// <summary>
/// Admin-only change to ER status and bed availability.
/// </summary>
public sealed class HospitalErUpdateRequest
{
    public HospitalStatus? ErStatus { get; set; }
    public int? AvailableBeds { get; set; }
}

public sealed record HospitalLocationDto(
    double Latitude,
    double Longitude,
    string? Address);

public sealed record HospitalDto(
    Guid Id,
    string Name,
    HospitalLocationDto Location,
    string ErStatus,
    int AvailableBeds,
    DateTime UpdatedAt,
    double? DistanceKm);
