using System;
using System.ComponentModel.DataAnnotations;
using MyApplication.Api.Enums;

namespace MyApplication.Api.Dtos;

// Matthew Romano - Nov 14th, 2026 - Extra/Util Dtos

/// <summary>
///  Holds a unit record as a Dto
/// </summary>
/// <param name="Id">id</param>
/// <param name="CallSign">unit callsign</param>
/// <param name="Type">unit type</param>
/// <param name="StationId">id of station unit belongs to</param>
/// <param name="Status">unit status</param>
public sealed record UnitDto(Guid Id, string CallSign, string Type, Guid StationId, string Status);

/// <summary>
/// Request to create a new unit
/// </summary>
/// <param name="CallSign">new unit callsign</param>
/// <param name="Type">new unit type</param>
/// <param name="StationId">station unit will belong to</param>
public sealed record UnitCreateRequest(
    [Required, MinLength(1)] string CallSign,
    UnitType? Type,
    Guid StationId);

/// <summary>
///  Request to update a unit
/// </summary>
/// <param name="CallSign">changed callsign</param>
/// <param name="Type">changed type</param>
/// <param name="StationId">changed station id</param>
/// <param name="Status">changed status</param>
public sealed record UnitUpdateRequest(
    [Required, MinLength(1)] string CallSign,
    UnitType? Type,
    Guid StationId,
    UnitStatus? Status);


/// <summary>
/// Station Dto definition
/// </summary>
/// <param name="Id">station id</param>
/// <param name="Name">station name</param>
/// <param name="Type">station type</param>
/// <param name="Latitude">station latitude</param>
/// <param name="Longitude">station longitude</param>
/// <param name="Address">station address</param>
/// <param name="Units">units belonging to statsion</param>
public sealed record StationDto(
    Guid Id, string Name, string Type, double Latitude, double Longitude,
    string? Address, IReadOnlyList<UnitDto> Units);
//shouldn't this hold a location object reference instead?

/// <summary>
/// Request to save a station with new information
/// </summary>
/// <param name="Name">changed name</param>
/// <param name="Type">changed type</param>
/// <param name="Latitude">changed lat</param>
/// <param name="Longitude">changed long</param>
/// <param name="Address">changed address</param>
public sealed record StationSaveRequest(
    [Required, MinLength(1)] string Name,
    StationType? Type,
    double Latitude, double Longitude, string? Address);

/// <summary>
/// Requests to assign units to a specific task
/// </summary>
/// <param name="UnitIds">units being requested</param>
public sealed record AssignUnitsRequest([Required, MinLength(1)] List<Guid> UnitIds);

/// <summary>
/// Dto that holds unit assignment info
/// </summary>
/// <param name="Id"> id</param>
/// <param name="IncidentId">id of incident</param>
/// <param name="UnitId">id of unit</param>
/// <param name="AssignedAt">timestampt assigned at</param>
/// <param name="ArrivedAt">timestamp arrived at</param>
/// <param name="ReleasedAt">timestamp released at</param>
public sealed record AssignmentDto(Guid Id, Guid IncidentId, Guid UnitId, DateTime AssignedAt,
    DateTime? ArrivedAt, DateTime? ReleasedAt);

/// <summary>
///  Holds the suggested unit for that type of incident
/// </summary>
/// <param name="Unit">the ideal suggested unit</param>
/// <param name="DistanceKm">distance of unit from incident</param>
public sealed record SuggestedUnitDto(UnitDto Unit, double DistanceKm);

/// <summary>
///  Holds the stats of all incidents
/// </summary>
/// <param name="From">time range start</param>
/// <param name="To">time range end</param>
/// <param name="TotalIncidents">total incidents in period</param>
/// <param name="ByType">type filtering</param>
/// <param name="ByStatus">status filtering</param>
/// <param name="AverageResponseMinutes">average response time for incidents, in minutes</param>
/// <param name="DeathCount">total deaths ocurred</param>
public sealed record StatsDto(
    DateTime From, DateTime To, long TotalIncidents,
    IReadOnlyDictionary<string, int> ByType,
    IReadOnlyDictionary<string, int> ByStatus,
    double? AverageResponseMinutes, int DeathCount);

/// <summary>
/// holds the information for a patient transport
/// </summary>
/// <param name="IncidentId">id of incident</param>
/// <param name="TriageLevel">triage level of patient</param>
/// <param name="HospitalId">hospital id patient was sent to</param>
/// <param name="ResidentId">resident id of patient</param>
public sealed record PatientTransportDto(
    Guid IncidentId, TriageLevel TriageLevel, Guid HospitalId, Guid? ResidentId);
