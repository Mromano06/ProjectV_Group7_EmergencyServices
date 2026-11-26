namespace MyApplication.Api.Dtos;

// Matthew Romano - Nov 14th, 2026 - Dto for Road Closures

// Dto to save a road closure
/// <summary>
/// ES-FR-010: a road closure near an incident, read from Public Transport's GET /road-closures
/// (their Swagger, Nov 25). Transit sends no coordinates, so most closures match by street.
/// </summary>
public sealed record NearbyRoadClosureDto(
    string Id, string? Description, DateTimeOffset? ExpectedEndAt,
    double? Latitude, double? Longitude, double? DistanceKm, // null = Transit sent no coordinates
    string MatchedBy); // "distance" (within 1 km) or "street" (same street as the incident)