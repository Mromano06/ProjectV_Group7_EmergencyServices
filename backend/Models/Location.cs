namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Location imp

/// <summary>
/// Owned EF Core value object.
/// Coordinates are WGS84 decimal degrees.
/// </summary>
public class Location
{
    /// <summary> lattitude of location </summary>
    public double Latitude { get; set; }

    /// <summary> longitude of location </summary>
    public double Longitude { get; set; }

    /// <summary> street address of location in city. optional </summary>
    public string? Address { get; set; }
}
