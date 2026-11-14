using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using MyApplication.Api.Data;
using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Services;

// Matthew Romano - Nov 14th, 2026 - Unit Services

/// <summary>ES-FR-007: unit reference data; no hard deletion.</summary>
public sealed class UnitService(EmergencyDbContext db) : IUnitService
{
    public static UnitDto ToDto(Unit u) => new(u.Id, u.CallSign, u.Type.ToString(), u.StationId, u.Status.ToString());

    // List all current units
    public async Task<PagedResult<UnitDto>> ListAsync(UnitStatus? status, UnitType? type, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = pageSize is >= 1 and <= 100 ? pageSize : 25;
        var query = db.Units.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(u => u.Status == status.Value);
        if (type.HasValue) query = query.Where(u => u.Type == type.Value);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(u => u.CallSign).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<UnitDto>(rows.Select(ToDto).ToList(), page, pageSize, count);
    }

    // Get a specific units info
    public async Task<UnitDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        return unit is null ? null : ToDto(unit);
    }

    // Creates a new unit
    public async Task<ServiceResult<UnitDto>> CreateAsync(UnitCreateRequest req, Guid userId, CancellationToken ct)
    {
        if (!req.Type.HasValue || !Enum.IsDefined(req.Type.Value))
            return ServiceResult<UnitDto>.Invalid("invalid_type", "Unit type is required: Engine, Ladder or Ambulance.");
        var error = await ValidateAsync(req.CallSign, req.Type.Value, req.StationId, null, ct);
        if (error is not null) return error;
        var unit = new Unit
        {
            CallSign = req.CallSign.Trim(), Type = req.Type.Value, StationId = req.StationId,
            Status = UnitStatus.Available, CreatedBy = userId
        };
        db.Units.Add(unit);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        { return ServiceResult<UnitDto>.Conflict("duplicate_callsign", "Call sign already exists."); }
        return ServiceResult<UnitDto>.Created(ToDto(unit));
    }

    // Updates an exisiting a new unit
    public async Task<ServiceResult<UnitDto>> UpdateAsync(Guid id, UnitUpdateRequest req, Guid userId, CancellationToken ct)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null) return ServiceResult<UnitDto>.Missing("unit_not_found", "Unit not found.");
        if (!req.Type.HasValue || !Enum.IsDefined(req.Type.Value))
            return ServiceResult<UnitDto>.Invalid("invalid_type", "Unit type is required: Engine, Ladder or Ambulance.");
        var error = await ValidateAsync(req.CallSign, req.Type.Value, req.StationId, id, ct);
        if (error is not null) return error;
        if (!req.Status.HasValue || !Enum.IsDefined(req.Status.Value))
            return ServiceResult<UnitDto>.Invalid("invalid_status", "Unit status is required.");
        var assigned = await db.IncidentAssignments.AnyAsync(a => a.UnitId == id && a.ReleasedAt == null, ct);
        if (assigned && (req.Status.Value == UnitStatus.Available || req.Status.Value == UnitStatus.OutOfService || req.StationId != unit.StationId))
            return ServiceResult<UnitDto>.Conflict("unit_assigned", "Release active assignments before changing availability or station.");
        if (!assigned && (req.Status.Value is UnitStatus.Dispatched or UnitStatus.OnScene))
            return ServiceResult<UnitDto>.Conflict("assignment_required", "Use the dispatch endpoint to dispatch a unit.");
        unit.CallSign = req.CallSign.Trim(); unit.Type = req.Type.Value; unit.StationId = req.StationId;
        unit.Status = req.Status.Value; unit.UpdatedBy = userId;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return ServiceResult<UnitDto>.Conflict("unit_changed", "Unit changed during update."); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        { return ServiceResult<UnitDto>.Conflict("duplicate_callsign", "Call sign already exists."); }
        return ServiceResult<UnitDto>.Ok(ToDto(unit));
    }

    // Deativates/fires a unit from the sation
    public async Task<ServiceResult<UnitDto>> DeactivateAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null) return ServiceResult<UnitDto>.Missing("unit_not_found", "Unit not found.");
        if (await db.IncidentAssignments.AnyAsync(a => a.UnitId == id && a.ReleasedAt == null, ct))
            return ServiceResult<UnitDto>.Conflict("unit_assigned", "Release this unit before deactivating it.");
        unit.Status = UnitStatus.OutOfService;
        unit.UpdatedBy = userId;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return ServiceResult<UnitDto>.Conflict("unit_changed", "Unit changed during deactivation."); }
        return ServiceResult<UnitDto>.Ok(ToDto(unit));
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Number is 2601 or 2627;

    // Unit Validation
    private async Task<ServiceResult<UnitDto>?> ValidateAsync(string? sign, UnitType type, Guid stationId, Guid? self, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sign)) return ServiceResult<UnitDto>.Invalid("missing_callsign", "Call sign is required.");
        if (!Enum.IsDefined(type)) return ServiceResult<UnitDto>.Invalid("invalid_type", "Invalid unit type.");
        if (stationId == Guid.Empty || !await db.Stations.AnyAsync(s => s.Id == stationId, ct))
            return ServiceResult<UnitDto>.Invalid("unknown_station", "Station does not exist.");
        if (await db.Units.AnyAsync(u => u.CallSign == sign.Trim() && (!self.HasValue || u.Id != self.Value), ct))
            return ServiceResult<UnitDto>.Conflict("duplicate_callsign", "Call sign already exists.");
        return null;
    }
}
