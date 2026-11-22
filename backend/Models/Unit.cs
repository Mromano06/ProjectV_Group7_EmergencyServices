using System.ComponentModel.DataAnnotations;
using MyApplication.Api.Enums;
namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Unit Definition
// Holds all relevant fields for unit, including type and location

/// <summary>
/// represents an emergency services unit that is associated with the system
/// ES-FR-007; 
/// RowVersion protects concurrent EF tracked updates.
/// </summary>
public class Unit : AuditableEntity
{
    /// <summary> string callsign of unit </summary>
    public string CallSign { get; set; } = string.Empty;

    /// <summary> unitype of unit (engine, ladder, ambulance, etc.) </summary>
    public UnitType Type { get; set; }

    /// <summary> id of station unit is associated with </summary>
    public Guid StationId { get; set; }

    /// <summary> reference of station unit is associated with </summary>
    public Station Station { get; set; } = null!;

    /// <summary> current status of unit (availabble, dispatched, etc.) </summary>
    public UnitStatus Status { get; set; } = UnitStatus.Available;

    /// <summary> rowversion </summary>
    [Timestamp] public byte[] RowVersion { get; set; } = [];
        // uh guys what is this ?
}
