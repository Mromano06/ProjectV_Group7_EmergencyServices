using System.ComponentModel.DataAnnotations.Schema;
using MyApplication.Api.Enums;


namespace MyApplication.Api.Models;

// Matthew Romano - Nov 7th, 2026 - Main Hospital Model

/// <summary>
/// Represents a hospital registered in the system
/// Capacity/status info can be updated with external calls
/// </summary>
public class Hospital : AuditableEntity
{
    /// <summary>Name of the hospital.</summary>
    public string Name {get; set;} = string.Empty;

    /// <summary>Hospital address/location.</summary>
    public Location Location {get; set;} = new();

    /// <summary>Total number of rooms in the hospital</summary>
    public HospitalStatus ErStatus {get; set;} = HospitalStatus.Open;

    /// <summary>Total number of free/remaining rooms.</summary>
    public int AvailableBeds {get; set;}

}