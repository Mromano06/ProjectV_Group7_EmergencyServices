using MyApplication.Api.Enums;
namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Station Definition
// Holds all relevant fields for unit, including type and location
public class Station : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public StationType Type { get; set; } = StationType.Fire;
    public Location Location { get; set; } = new();
    public List<Unit> Units { get; set; } = [];
}
