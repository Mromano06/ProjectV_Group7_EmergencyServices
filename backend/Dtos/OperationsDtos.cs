using System.ComponentModel.DataAnnotations;
using MyApplication.Api.Enums;

namespace MyApplication.Api.Dtos;

// Matthew Romano - Nov 14th, 2026 - Extra/Util Dtos

// Holds a unit record as a Dto
public sealed record UnitDto(Guid Id, string CallSign, string Type, Guid StationId, string Status);

// Request to create a new unit
public sealed record UnitCreateRequest(
    [Required, MinLength(1)] string CallSign,
    UnitType? Type,
    Guid StationId);

// Request to update a unit
public sealed record UnitUpdateRequest(
    [Required, MinLength(1)] string CallSign,
    UnitType? Type,
    Guid StationId,
    UnitStatus? Status);


// Station Dto definition
public sealed record StationDto(
    Guid Id, string Name, string Type, double Latitude, double Longitude,
    string? Address, IReadOnlyList<UnitDto> Units);

// Request to save a station with new information
public sealed record StationSaveRequest(
    [Required, MinLength(1)] string Name,
    StationType? Type,
    double Latitude, double Longitude, string? Address);

// Requests to assign units to a specific task
public sealed record AssignUnitsRequest([Required, MinLength(1)] List<Guid> UnitIds);

// Dto that holds unit assignment info
public sealed record AssignmentDto(Guid Id, Guid IncidentId, Guid UnitId, DateTime AssignedAt,
    DateTime? ArrivedAt, DateTime? ReleasedAt);

// Holds the suggested unit for that type of incident
public sealed record SuggestedUnitDto(UnitDto Unit, double DistanceKm);

// Holds the stats of all incidents
public sealed record StatsDto(
    DateTime From, DateTime To, long TotalIncidents,
    IReadOnlyDictionary<string, int> ByType,
    IReadOnlyDictionary<string, int> ByStatus,
    double? AverageResponseMinutes, int DeathCount);
