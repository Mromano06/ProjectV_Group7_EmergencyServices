using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Data;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Services;

// Matthew Romano - 25th Nov, 2026 - Hospital Service Contoller

/// <summary>
/// Handles hospital info and capacity management.
/// External hospital clients provide information REST API calls.
/// ES-FR-012/013: ER status, available beds, nearest-open suggestions.
/// </summary>
public class HospitalService : IHospitalService
{
    private readonly EmergencyDbContext _db;
    private readonly ILogger<HospitalService> _logger;

    public HospitalService(EmergencyDbContext db, ILogger<HospitalService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Reading
    /// <summary>Retrieve all registered hospitals.</summary>
    public async Task<List<Hospital>> GetAllAsync()
    {
        return await _db.Hospitals
            .AsNoTracking()
            .OrderBy(h => h.Name)
            .ToListAsync();
    }

    /// <summary>Retrieve a hospital using its ID.</summary>
    public async Task<Hospital?> GetByIdAsync(Guid id)
    {
        return await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == id);
    }

    /// <summary>
    /// Search hospitals. When coordinates are supplied, sort by straight-line
    /// distance from that position, not road travel time.
    /// </summary>
    public async Task<List<HospitalSearchResult>> SearchAsync(
        double? latitude, double? longitude, bool openOnly, CancellationToken ct)
    {
        IQueryable<Hospital> query = _db.Hospitals.AsNoTracking();

        if (openOnly)
            query = query.Where(h => h.ErStatus == HospitalStatus.Open);

        var hospitals = await query.ToListAsync(ct);
        bool hasCoordinates = latitude.HasValue && longitude.HasValue;

        var results = hospitals.Select(h => new HospitalSearchResult(
            h,
            hasCoordinates ? DistanceKm(latitude!.Value, longitude!.Value,
                h.Location.Latitude, h.Location.Longitude) : null));

        return (hasCoordinates
                ? results.OrderBy(r => r.DistanceKm).ThenBy(r => r.Hospital.Name)
                : results.OrderBy(r => r.Hospital.Name))
            .ToList();
    }

    // Creating
    /// <summary>
    /// Register a hospital using info provided
    /// by the external hospital client.
    /// </summary>
    public async Task<Hospital> CreateAsync(Hospital hospital, CancellationToken ct = default)
    {
        ValidateHospital(hospital);

        var newHospital = new Hospital
        {
            Name = hospital.Name.Trim(),
            Location = CopyLocation(hospital.Location),
            ErStatus = hospital.ErStatus,
            AvailableBeds = hospital.AvailableBeds
        };

        _db.Hospitals.Add(newHospital);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Hospital {Id} ({Name}) registered", newHospital.Id, newHospital.Name);
        return newHospital;
    }

    // Updating
    /// <summary>
    /// Update a hospital's details, capacity, and status.
    /// Returns null if the hospital does not exist.
    /// </summary>
    public async Task<Hospital?> UpdateAsync(Guid id, Hospital hospital, CancellationToken ct = default)
    {
        ValidateHospital(hospital);

        var existing = await _db.Hospitals.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (existing is null)
            return null;

        existing.Name = hospital.Name.Trim();
        existing.Location.Latitude = hospital.Location.Latitude;
        existing.Location.Longitude = hospital.Location.Longitude;
        existing.Location.Address = hospital.Location.Address?.Trim();
        existing.ErStatus = hospital.ErStatus;
        existing.AvailableBeds = hospital.AvailableBeds;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Hospital {Id} ({Name}) updated", existing.Id, existing.Name);
        return existing;
    }

    /// <summary>Update only the ER status and available beds (ES-FR-012).</summary>
    public async Task<Hospital?> UpdateErAsync(
        Guid id, HospitalStatus status, int availableBeds, CancellationToken ct)
    {
        if (!Enum.IsDefined(status) || availableBeds < 0)
            throw new ArgumentException("Invalid ER status or available beds.");

        var hospital = await _db.Hospitals.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (hospital is null)
            return null;

        hospital.ErStatus = status;
        hospital.AvailableBeds = availableBeds;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Hospital {Id} ER updated to {Status} with {Beds} beds",
            hospital.Id, hospital.ErStatus, hospital.AvailableBeds);
        return hospital;
    }

    // Validation
    /// <summary>Ensures hospital information is valid before saving.</summary>
    private static void ValidateHospital(Hospital hospital)
    {
        ArgumentNullException.ThrowIfNull(hospital);

        if (string.IsNullOrWhiteSpace(hospital.Name) || hospital.Name.Trim().Length > 200)
            throw new ArgumentException("Hospital name is required (max 200 characters).");

        if (hospital.Location is null ||
            !ValidCoordinate(hospital.Location.Latitude, -90, 90) ||
            !ValidCoordinate(hospital.Location.Longitude, -180, 180))
            throw new ArgumentException("Hospital location requires valid coordinates.");

        if (hospital.Location.Address?.Length > 500)
            throw new ArgumentException("Hospital address is too long (max 500 characters).");

        if (hospital.AvailableBeds < 0)
            throw new ArgumentException("Available beds cannot be negative.");

        if (!Enum.IsDefined(hospital.ErStatus))
            throw new ArgumentException("Invalid hospital ER status.");
    }

    private static Location CopyLocation(Location location) => new()
    {
        Latitude = location.Latitude,
        Longitude = location.Longitude,
        Address = location.Address?.Trim()
    };

    private static bool ValidCoordinate(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;

    // Haversine distance, in km (straight-line geographical distance).
    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        static double Radians(double value) => value * Math.PI / 180.0;
        const double earthRadiusKm = 6371.0088;
        var dLat = Radians(lat2 - lat1);
        var dLon = Radians(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
            Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2)) *
            Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * earthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
