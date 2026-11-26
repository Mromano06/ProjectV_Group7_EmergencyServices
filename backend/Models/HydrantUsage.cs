namespace MyApplication.Api.Models;

// sebastian solorzano, nov 21, hydrant usage model

/// <summary>
/// represents the usage of a fire hydrant after/for an incident
/// </summary>
public class HydrantUsage : AuditableEntity
{
    /// <summary> id of the incident </summary>
    public Guid IncidentId { get; set; }

    /// <summary> id of the fire hydrant used </summary>
    public Guid HydrantId { get; set; }
        //im gonna assume hydrant ids belong to maintenance? since we don't got 'em

    /// <summary> litres of water used from hydrant </summary>
    public double LitresUsed { get; set; }

    /// <summary>
    /// when Maintenance accepted the usage report (ES-FR-016).
    /// null = not sent yet, so a failed send can be retried later.
    /// </summary>
    public DateTime? SentToMaintenanceAt { get; set; }
}