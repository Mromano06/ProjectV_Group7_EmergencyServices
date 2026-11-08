namespace MyApplication.Api.Models;

/// <summary>
/// Links an incident to the warning it is related to
/// This is its own small table so we do not have to change the Incident class,
/// which belongs to the incidents part of the project. One incident can be linked to
/// one or more warnings, and a warning can have many incidents.
/// </summary>
public class WarningIncidentLink : AuditableEntity
{
    public Guid WeatherWarningId { get; set; }
    public WeatherWarning? WeatherWarning { get; set; }

    /// <summary>The Incident's Id. Stored as a plain Guid (no foreign key yet)</summary>
    public Guid IncidentId { get; set; }
}
