namespace MyApplication.Api.Dtos;

// Matthew Romano - Nov 14th, 2026 - Dto for Road Closures

// Dto to save a road closure
/// <summary>
/// ES-FR-010: adapter result. Field names coming from Maintenance are provisional
/// until that team's SH-C03 JSON contract is finalized.
/// </summary>
public sealed record NearbyRoadClosureDto(
    string Id, string? Description, DateTimeOffset? ExpectedEndAt,
    double Latitude, double Longitude, double DistanceKm);