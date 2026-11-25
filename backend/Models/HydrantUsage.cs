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

    /// <summary> litres of water used from hydrant </summary>
    public double LitresUsed { get; set; }

    /// <summary> timestamp for maintenance </summary>
    public DateTime? SentToMaintenanceAt { get; set; }

    /// <summary> number of attempted sends to Maintenance </summary>
    public int DeliveryAttempts {get; set;}

    /// <summary> timestamp of last send attampt <summary>
    public DateTime? LastAttempt { get; set; }

    /// <summary> last dilivery error for troubleshooting <summary>
    public string? LastDeliveryError { get; set; }

}