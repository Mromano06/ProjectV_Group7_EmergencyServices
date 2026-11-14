using System.ComponentModel.DataAnnotations;
using MyApplication.Api.Enums;

namespace MyApplication.Api.Dtos;

// Matthew Romano - Nov 14th, 2026 - Incident DTO

// Locational record
public sealed record IncidentLocationRequest(double Latitude, double Longitude, string? Address);

// Creates a new incident
public sealed record IncidentCreateRequest(
    IncidentType? Type,
    IncidentSeverity? Severity,
    [Required] IncidentLocationRequest? Location,
    string? Description,
    string? CallerName,
    string? CallerPhone,
    Guid? SourceCallId);

// A request to change the status of the incident
public sealed record ChangeIncidentStatusRequest(IncidentStatus? Status);

// Saves the info from the event/incident 
public sealed record FileAfterActionReportRequest(
    [Required, MinLength(1)] string Outcome,
    [Range(0, int.MaxValue)] int Injuries,
    [Range(0, int.MaxValue)] int Deaths,
    [Range(0, double.MaxValue)] double HydrantWaterLitres,
    string? Notes);

public sealed record IncidentSummaryDto(Guid Id, string Type, int Severity, string Status,
    double Latitude, double Longitude, DateTime ReportedAt);

// Holds the status history of the incident
public sealed record IncidentStatusHistoryDto(string FromStatus, string ToStatus, Guid ChangedBy, DateTime ChangedAt);

// Records the actual events/data to do with the incident
public sealed record AfterActionReportDto(string Outcome, int Injuries, int Deaths,
    double HydrantWaterLitres, string? Notes, Guid FiledBy, DateTime FiledAt);

// Holds the details for the specific incident
public sealed record IncidentDetailDto(
    Guid Id, string Type, int Severity, string Status, IncidentLocationRequest Location,
    string? Description, string? CallerName, string? CallerPhone, Guid? SourceCallId,
    DateTime ReportedAt, DateTime? ClosedAt,
    IReadOnlyList<IncidentStatusHistoryDto> History,
    IReadOnlyList<AssignmentDto> Assignments,
    AfterActionReportDto? Report);
