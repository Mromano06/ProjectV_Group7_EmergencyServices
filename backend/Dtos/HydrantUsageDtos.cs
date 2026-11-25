namespace MyApplication.Api.Dtos;

// ES-FR-016: the incident ID belongs to the enclosing incident/report.
// Hydrant IDs are owned by Maintenance, not by our own Hydrant table.
public sealed class HydrantUsageRequest
{
    public Guid HydrantId { get; set; }
    public double? LitresUsed { get; set; }
}

public sealed record HydrantUsageDto(
    Guid Id,
    Guid IncidentId,
    Guid HydrantId,
    double LitresUsed,
    DateTime? SentToMaintenanceAt,
    int DeliveryAttempts,
    DateTime? LastAttemptAt);
