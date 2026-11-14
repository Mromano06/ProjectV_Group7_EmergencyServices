using System.ComponentModel.DataAnnotations;
using MyApplication.Api.Enums;
namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Main Incident Definition
// Holds all relevant fields for the incident
public class Incident : AuditableEntity
{
    public IncidentType Type { get; set; }
    public IncidentSeverity Severity { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Reported;
    public Location Location { get; set; } = new();
    public string? Description { get; set; }
    public string? CallerName { get; set; }
    public string? CallerPhone { get; set; }
    public Guid? SourceCallId { get; set; }
    public DateTime ReportedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public List<IncidentAssignment> Assignments { get; set; } = [];
    public List<IncidentStatusHistory> StatusHistory { get; set; } = [];
    public AfterActionReport? Report { get; set; }
}
