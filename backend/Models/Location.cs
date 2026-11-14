namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Location imp

/// <summary>
/// Owned EF Core value object.
/// Coordinates are WGS84 decimal degrees.
/// </summary>
public class Location
{
    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string? Address { get; set; }
}
