using System.ComponentModel.DataAnnotations;
using MyApplication.Api.Enums;
namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Unit Definition
// Holds all relevant fields for unit, including type and location
/// <summary>ES-FR-007; RowVersion protects concurrent EF tracked updates.</summary>
public class Unit : AuditableEntity
{
    public string CallSign { get; set; } = string.Empty;
    public UnitType Type { get; set; }
    public Guid StationId { get; set; }
    public Station Station { get; set; } = null!;
    public UnitStatus Status { get; set; } = UnitStatus.Available;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
