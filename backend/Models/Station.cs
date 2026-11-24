using MyApplication.Api.Enums;
namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Station Definition
// Holds all relevant fields for unit, including type and location

/// <summary> 
/// represents a fire or EMS station registered in the system
/// req. ES-FR-011
/// </summary>
public class Station : AuditableEntity
{
    /// <summary> string name of the station </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> station type (fire/EMS) </summary>
    public StationType Type { get; set; } = StationType.Fire;

    /// <summary> location of the station </summary>
    public Location Location { get; set; } = new();

    /// <summary> list of units associated with the station </summary>
    public List<Unit> Units { get; set; } = [];
}
