using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Data;
using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Services;

// Matthew Romano - Nov 14th, 2026 - Dispatch Services

/// <summary>
/// ES-FR-008, ES-FR-009, ES-NFR-006. Atomic conditional unit claims + a filtered
/// SQL Server uniqueness constraint ensure a unit cannot have two active assignments.
/// </summary>
public sealed class DispatchService(EmergencyDbContext db) : IDispatchService
{
    // Maps incidents information
    // TODO: Determione if we're using Address or Lat Long and imp 
    private static AssignmentDto Map(IncidentAssignment a) =>
        new(a.Id, a.IncidentId, a.UnitId, a.AssignedAt, a.ArrivedAt, a.ReleasedAt);

    // Main task assignment function, allows unit assignment to an incident
    public async Task<ServiceResult<IReadOnlyList<AssignmentDto>>> AssignAsync(
        Guid incidentId, AssignUnitsRequest request, Guid userId, CancellationToken ct)
    {
        if (request.UnitIds is null || request.UnitIds.Count == 0 || request.UnitIds.Any(x => x == Guid.Empty)
            || request.UnitIds.Distinct().Count() != request.UnitIds.Count)
            return ServiceResult<IReadOnlyList<AssignmentDto>>.Invalid("invalid_units", "Provide one or more distinct, valid unit IDs.");

        // One transaction for every unit + incident status. Any failed claim rolls back all claims.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var incident = await db.Incidents.FirstOrDefaultAsync(x => x.Id == incidentId, ct);
        if (incident is null)
            return ServiceResult<IReadOnlyList<AssignmentDto>>.Missing("incident_not_found", "Incident not found.");
        if (incident.Status is IncidentStatus.Closed or IncidentStatus.Resolved)
            return ServiceResult<IReadOnlyList<AssignmentDto>>.Conflict("incident_inactive", "Cannot dispatch to a resolved or closed incident.");

        var now = DateTime.UtcNow;
        var assignments = new List<IncidentAssignment>();
        try
        {
            // Ordering reduces deadlock risk when > 1 units are assigned.
            foreach (var id in request.UnitIds.OrderBy(x => x))
            {
                // UPDATE is atomic and checked by rows-affected
                // Does not use the EF change tracker, so UpdateBy/UpdatedAt explicitly.
                var claimed = await db.Units
                    .Where(u => u.Id == id && u.Status == UnitStatus.Available &&
                        !db.IncidentAssignments.Any(a => a.UnitId == id && a.ReleasedAt == null))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.Status, UnitStatus.Dispatched)
                        .SetProperty(u => u.UpdatedBy, userId)
                        .SetProperty(u => u.UpdatedAt, now), ct);
                if (claimed != 1)
                    return ServiceResult<IReadOnlyList<AssignmentDto>>.Conflict("unit_unavailable", $"Unit {id} is unavailable or does not exist.");

                var assignment = new IncidentAssignment
                {
                    IncidentId = incidentId, UnitId = id, AssignedAt = now, CreatedBy = userId
                };
                db.IncidentAssignments.Add(assignment);
                assignments.Add(assignment);
            }

            if (incident.Status == IncidentStatus.Reported)
            {
                db.IncidentStatusHistories.Add(new IncidentStatusHistory
                {
                    IncidentId = incidentId, FromStatus = IncidentStatus.Reported,
                    ToStatus = IncidentStatus.Dispatched, ChangedBy = userId,
                    ChangedAt = now, CreatedBy = userId
                });
                incident.Status = IncidentStatus.Dispatched;
                incident.UpdatedBy = userId;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        { return ServiceResult<IReadOnlyList<AssignmentDto>>.Conflict("assignment_conflict", "A concurrent request changed the incident or unit."); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        { return ServiceResult<IReadOnlyList<AssignmentDto>>.Conflict("unit_unavailable", "A unit has already been assigned elsewhere."); }
        catch (SqlException ex) when (ex.Number == 1205)
        { return ServiceResult<IReadOnlyList<AssignmentDto>>.Conflict("assignment_conflict", "Another dispatch modified this unit. Retry the request."); }

        return ServiceResult<IReadOnlyList<AssignmentDto>>.Created(assignments.Select(Map).ToList());
    }

    // Releases an assigned unit from thier current incident
    public async Task<ServiceResult<AssignmentDto>> ReleaseAsync(Guid incidentId, Guid assignmentId, Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var assignment = await db.IncidentAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.IncidentId == incidentId, ct);
        if (assignment is null) return ServiceResult<AssignmentDto>.Missing("assignment_not_found", "Assignment not found.");
        if (assignment.ReleasedAt.HasValue) return ServiceResult<AssignmentDto>.Conflict("already_released", "Assignment was already released.");
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == assignment.UnitId, ct);
        if (unit is null) return ServiceResult<AssignmentDto>.Conflict("unit_missing", "Assigned unit is missing.");
        assignment.ReleasedAt = DateTime.UtcNow;
        unit.Status = UnitStatus.Available;
        unit.UpdatedBy = userId;
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { return ServiceResult<AssignmentDto>.Conflict("assignment_conflict", "Another request changed this assignment or unit."); }
        return ServiceResult<AssignmentDto>.Ok(Map(assignment));
    }

    // Lists all of the assignmesnts associated with an incident
    public async Task<ServiceResult<IReadOnlyList<AssignmentDto>>> ListAsync(Guid incidentId, CancellationToken ct)
    {
        if (!await db.Incidents.AsNoTracking().AnyAsync(i => i.Id == incidentId, ct))
            return ServiceResult<IReadOnlyList<AssignmentDto>>.Missing("incident_not_found", "Incident not found.");
        var rows = await db.IncidentAssignments.AsNoTracking()
            .Where(a => a.IncidentId == incidentId).OrderBy(a => a.AssignedAt).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<AssignmentDto>>.Ok(rows.Select(Map).ToList());
    }

    // Get, and list units based on distance from the incident
    public async Task<ServiceResult<IReadOnlyList<SuggestedUnitDto>>> SuggestAsync(Guid incidentId, UnitType type, CancellationToken ct)
    {
        if (!Enum.IsDefined(type)) return ServiceResult<IReadOnlyList<SuggestedUnitDto>>.Invalid("invalid_type", "Unknown unit type.");
        var incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return ServiceResult<IReadOnlyList<SuggestedUnitDto>>.Missing("incident_not_found", "Incident not found.");
        var units = await db.Units.AsNoTracking()
            .Where(u => u.Status == UnitStatus.Available && u.Type == type &&
                !db.IncidentAssignments.Any(a => a.UnitId == u.Id && a.ReleasedAt == null))
            .Join(db.Stations.AsNoTracking(), u => u.StationId, s => s.Id, (u, s) => new { u, s })
            .ToListAsync(ct);
        // No unit GPS coordinates in Data_Model; use station's coordinates as an approximation.
        var suggestions = units.Select(x => new SuggestedUnitDto(UnitService.ToDto(x.u),
                DistanceKm(incident.Location.Latitude, incident.Location.Longitude,
                    x.s.Location.Latitude, x.s.Location.Longitude)))
            .OrderBy(x => x.DistanceKm).ToList();
        return ServiceResult<IReadOnlyList<SuggestedUnitDto>>.Ok(suggestions);
    }

    // Helper to calculate the distance of two coords
    // TODO: Determine if this is nessecary, current location imp uses address
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double radiusKm = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1 * Math.PI / 180)
            * Math.Cos(lat2 * Math.PI / 180) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * radiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Number is 2601 or 2627;
}
