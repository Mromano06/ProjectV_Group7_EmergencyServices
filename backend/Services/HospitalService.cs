
using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Data;
using MyApplication.Api.Models;

namespace MyApplication.Api.Services;

/// <summary>
/// Handles hospital info and capacity management.
/// External hospital clients provide information REST API calls.
/// </summary>
public class HospitalService : IHospitalService
{
    private readonly EmergencyDbContext _db;
    private readonly ILogger<HospitalService> _logger;

    public HospitalService(
        EmergencyDbContext db,
        ILogger<HospitalService> logger)
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

    // Creating
    /// <summary>
    /// Register a hospital using info provided
    /// by the external hospital client.
    /// </summary>
    public async Task<Hospital> CreateAsync(Hospital hospital)
    {
        ValidateHospital(hospital);

        var newHospital = new Hospital
        {
            Name = hospital.Name.Trim(),
            Location = hospital.Location.Trim(),
            TotalRooms = hospital.TotalRooms,
            OccupiedRooms = hospital.OccupiedRooms,
            Status = hospital.Status
        };

        _db.Hospitals.Add(newHospital);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Hospital {Id} ({Name}) registered",
            newHospital.Id,
            newHospital.Name);

        return newHospital;
    }

    // Updating
    /// <summary>
    /// Update a hospital's details, capacity, and status.
    /// Returns null if the hospital does not exist.
    /// </summary>
    public async Task<Hospital?> UpdateAsync(
        Guid id,
        Hospital hospital)
    {
        var existing = await _db.Hospitals
            .FirstOrDefaultAsync(h => h.Id == id);

        if (existing == null)
        {
            return null;
        }

        ValidateHospital(hospital);

        existing.Name = hospital.Name.Trim();
        existing.Location = hospital.Location.Trim();
        existing.TotalRooms = hospital.TotalRooms;
        existing.OccupiedRooms = hospital.OccupiedRooms;
        existing.Status = hospital.Status;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Hospital {Id} ({Name}) updated",
            existing.Id,
            existing.Name);

        return existing;
    }

    // Validation
    /// <summary>
    /// Ensures hospital information is valid before saving.
    /// </summary>
    private static void ValidateHospital(Hospital hospital)
    {
        ArgumentNullException.ThrowIfNull(hospital);

        if (string.IsNullOrWhiteSpace(hospital.Name))
        {
            throw new ArgumentException(
                "Hospital name is required.");
        }

        if (string.IsNullOrWhiteSpace(hospital.Location))
        {
            throw new ArgumentException(
                "Hospital location is required.");
        }

        if (hospital.TotalRooms < 0 ||
            hospital.OccupiedRooms < 0)
        {
            throw new ArgumentException(
                "Room counts cannot be negative.");
        }

        if (hospital.OccupiedRooms > hospital.TotalRooms)
        {
            throw new ArgumentException(
                "Occupied rooms cannot exceed total rooms.");
        }

        if (!Enum.IsDefined(hospital.Status))
        {
            throw new ArgumentException(
                "Invalid hospital status.");
        }
    }
}
