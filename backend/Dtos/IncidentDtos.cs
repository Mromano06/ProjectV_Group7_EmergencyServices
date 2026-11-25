using System;
using System.ComponentModel.DataAnnotations;
using MyApplication.Api.Enums;

namespace MyApplication.Api.Dtos;

// Matthew Romano - Nov 14th, 2026 - Incident DTO

/// <summary>
///  Locational record
/// </summary>
/// <param name="Latitude">lat of incident</param>
/// <param name="Longitude">long of incident</param>
/// <param name="Address">address of incident</param>
public sealed record IncidentLocationRequest(double Latitude, double Longitude, string? Address);

/// <summary>
///  Creates a new incident
/// </summary>
/// <param name="Type">incident type</param>
/// <param name="Severity">incident severity</param>
/// <param name="Location">location of incident</param>
/// <param name="Description">additional description of incident</param>
/// <param name="CallerName">name of caller who reported incident</param>
/// <param name="CallerPhone">phone number of caller who reported incident</param>
/// <param name="SourceCallId">id of source call</param>
public sealed record IncidentCreateRequest(
    IncidentType? Type,
    IncidentSeverity? Severity,
    [Required] IncidentLocationRequest? Location,
    string? Description,
    string? CallerName,
    string? CallerPhone,
    Guid? SourceCallId);

/// <summary>
/// A request to change the status of the incident
/// </summary>
/// <param name="Status">status change</param>
public sealed record ChangeIncidentStatusRequest(IncidentStatus? Status);

/// <summary>
/// Saves the info from the event/incident 
/// </summary>
/// <param name="Outcome">outcome of the incident</param>
/// <param name="Injuries">number of injuries that occurred</param>
/// <param name="Deaths">number of deaths that occurred</param>
/// <param name="HydrantWaterLitres">litres of water from fire hydrants used</param>
/// <param name="Notes">additional notes</param>
public sealed record FileAfterActionReportRequest(
    [Required, MinLength(1)] string Outcome,
    [Range(0, int.MaxValue)] int Injuries,
    [Range(0, int.MaxValue)] int Deaths,
    [Range(0, double.MaxValue)] double HydrantWaterLitres,
    string? Notes);

/// <summary>
/// summary of an incident
/// </summary>
/// <param name="Id">the id</param>
/// <param name="Type">incident type</param>
/// <param name="Severity">severity of incideng</param>
/// <param name="Status">status of incident</param>
/// <param name="Latitude">latitude</param>
/// <param name="Longitude">longitude</param>
/// <param name="ReportedAt">time it was reported at</param>
public sealed record IncidentSummaryDto(Guid Id, string Type, int Severity, string Status,
    double Latitude, double Longitude, DateTime ReportedAt);

/// <summary>
/// Holds a status history record for an incident
/// </summary>
/// <param name="FromStatus">before status</param>
/// <param name="ToStatus">after status</param>
/// <param name="ChangedBy">change author</param>
/// <param name="ChangedAt">change timestamp</param>
public sealed record IncidentStatusHistoryDto(string FromStatus, string ToStatus, Guid ChangedBy, DateTime ChangedAt);

/// <summary>
///  Records the actual events/data to do with the incident
/// </summary>
/// <param name="Outcome">outcome of the incident</param>
/// <param name="Injuries">number of injuries that occurred</param>
/// <param name="Deaths">number of deaths that occurred</param>
/// <param name="HydrantWaterLitres">litres of water from fire hydrants used</param>
/// <param name="Notes">additional notes</param>
/// <param name="FiledBy">report author</param>
/// <param name="FiledAt">report creation timestamp</param>
public sealed record AfterActionReportDto(string Outcome, int Injuries, int Deaths,
    double HydrantWaterLitres, string? Notes, Guid FiledBy, DateTime FiledAt);

/// <summary>
///  Holds the details for the specific incident
/// </summary>
/// <param name="Id">incident id</param>
/// <param name="Type">incident type</param>
/// <param name="Severity">incident severity</param>
/// <param name="Status">incident status</param>
/// <param name="Location">incident location</param>
/// <param name="Description">additional incident description</param>
/// <param name="CallerName">name of caller that reported incident</param>
/// <param name="CallerPhone">phone of caller that reported incident</param>
/// <param name="SourceCallId">source caller id</param>
/// <param name="ReportedAt">time reported at</param>
/// <param name="ClosedAt">time closed at</param>
/// <param name="History">incident status history</param>
/// <param name="Assignments">units assigned to incident</param>
/// <param name="Report">after action report for incident</param>
public sealed record IncidentDetailDto(
    Guid Id, string Type, int Severity, string Status, IncidentLocationRequest Location,
    string? Description, string? CallerName, string? CallerPhone, Guid? SourceCallId,
    DateTime ReportedAt, DateTime? ClosedAt,
    IReadOnlyList<IncidentStatusHistoryDto> History,
    IReadOnlyList<AssignmentDto> Assignments,
    AfterActionReportDto? Report);

/// <summary>
/// holds details for hydrant usage of an incident
/// </summary>
/// <param name="IncidentId">id of incident</param>
/// <param name="HydrantId">id of hydrant used</param>
/// <param name="LitresUsed">litres of water used</param>
/// <param name="SentToMaintenanceAt">when Maintenance accepted it; null if not sent yet</param>
public sealed record HydrantUsageDto(
    Guid IncidentId, Guid HydrantId, double LitresUsed, DateTime? SentToMaintenanceAt
    );
//want to change the doubles in the report to a reference to one of these but not sure yet if that'll mess things up?
