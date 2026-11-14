using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Data;
using MyApplication.Api.Dtos;
using MyApplication.Api.Models;
using MyApplication.Api.Enums;

namespace MyApplication.Api.Services;

// Matthew Romano - Nov 14th, 2026 - Station Services
/// <summary>ES-FR-011; adapt Location fields to the existing Location.cs shape if needed.</summary>
public sealed class StationService(EmergencyDbContext db) : IStationService
{
    // Maps a location to a specific location
    // TODO: Determione if we're using Address or Lat Long and imp
    private static StationDto Map(Station s, IReadOnlyList<UnitDto> units) =>
        new(s.Id, s.Name, s.Type.ToString(), s.Location.Latitude, s.Location.Longitude, s.Location.Address, units);

    // Gets all current stations as well as thier assigned units and location
    public async Task<IReadOnlyList<StationDto>> GetAllAsync(CancellationToken ct)
    {
        var stations = await db.Stations.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var units = await db.Units.AsNoTracking().ToListAsync(ct);
        return stations.Select(s => Map(s, units.Where(u => u.StationId == s.Id).Select(UnitService.ToDto).ToList())).ToList();
    }

    // Get a single station with location and units
    public async Task<StationDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Stations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return null;
        var units = await db.Units.AsNoTracking().Where(x => x.StationId == id).ToListAsync(ct);
        return Map(s, units.Select(UnitService.ToDto).ToList());
    }

    // Handles logic to create a new station
    public async Task<ServiceResult<StationDto>> CreateAsync(StationSaveRequest req, Guid userId, CancellationToken ct)
    {
        var error = Validate(req);
        if (error is not null) return error;
        var station = new Station
        {
            Name = req.Name.Trim(), Type = req.Type!.Value,
            Location = new Location { Latitude = req.Latitude, Longitude = req.Longitude, Address = req.Address?.Trim() },
            CreatedBy = userId
        };
        db.Stations.Add(station);
        await db.SaveChangesAsync(ct);
        return ServiceResult<StationDto>.Created(Map(station, []));
    }

    // Handles logic to update an exisiting station
    public async Task<ServiceResult<StationDto>> UpdateAsync(Guid id, StationSaveRequest req, Guid userId, CancellationToken ct)
    {
        var error = Validate(req);
        if (error is not null) return error;
        var station = await db.Stations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (station is null) return ServiceResult<StationDto>.Missing("station_not_found", "Station not found.");
        station.Name = req.Name.Trim(); station.Type = req.Type!.Value;
        station.Location.Latitude = req.Latitude;
        station.Location.Longitude = req.Longitude;
        station.Location.Address = req.Address?.Trim();
        station.UpdatedBy = userId;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { return ServiceResult<StationDto>.Conflict("station_changed", "Station changed concurrently."); }
        var units = await db.Units.AsNoTracking().Where(x => x.StationId == id).ToListAsync(ct);
        return ServiceResult<StationDto>.Ok(Map(station, units.Select(UnitService.ToDto).ToList()));
    }

    // Handles logic to validate an exisiting station
    private static ServiceResult<StationDto>? Validate(StationSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return ServiceResult<StationDto>.Invalid("missing_name", "Station name required.");
        if (!req.Type.HasValue || !Enum.IsDefined(req.Type.Value))
            return ServiceResult<StationDto>.Invalid("invalid_type", "Station type must be Fire or EMS.");
        if (req.Latitude is < -90 or > 90 || req.Longitude is < -180 or > 180 ||
            double.IsNaN(req.Latitude) || double.IsNaN(req.Longitude) ||
            double.IsInfinity(req.Latitude) || double.IsInfinity(req.Longitude))
            return ServiceResult<StationDto>.Invalid("invalid_location", "Invalid latitude or longitude.");
        return null;
    }
}
