using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Data;
using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Incident Services

namespace MyApplication.Api.Services;

/// <summary>
/// Minimal Incident implementation supporting dispatch and reporting.
/// ES-FR-001/002/003/004/006; integrations ES-FR-015 through -020 require separate contracts.
/// </summary>
public sealed class IncidentService(EmergencyDbContext db) : IIncidentService
{
    public async Task<PagedResult<IncidentSummaryDto>> ListAsync(IncidentStatus? status, IncidentType? type, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = pageSize is >= 1 and <= 100 ? pageSize : 25;
        var query = db.Incidents.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(i => i.Status == status.Value);
        else query = query.Where(i => i.Status != IncidentStatus.Closed); // live board default
        if (type.HasValue) query = query.Where(i => i.Type == type.Value);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(i => i.Severity).ThenBy(i => i.ReportedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<IncidentSummaryDto>(rows.Select(Summarize).ToList(), page, pageSize, count);
    }

    public async Task<IncidentDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var incident = await db.Incidents.AsNoTracking()
            .Include(i => i.StatusHistory).Include(i => i.Assignments).Include(i => i.Report)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        return incident is null ? null : Detail(incident);
    }

    public async Task<ServiceResult<IncidentDetailDto>> CreateAsync(IncidentCreateRequest request, Guid userId, CancellationToken ct)
    {
        if (!request.Type.HasValue || !Enum.IsDefined(request.Type.Value) ||
            !request.Severity.HasValue || !Enum.IsDefined(request.Severity.Value))
            return ServiceResult<IncidentDetailDto>.Invalid("invalid_type_or_severity", "Provide a recognized type and severity (1 through 4).");
        if (request.Location is null || !CoordinatesValid(request.Location.Latitude, request.Location.Longitude))
            return ServiceResult<IncidentDetailDto>.Invalid("invalid_location", "Valid latitude and longitude are required.");
        if (string.IsNullOrWhiteSpace(request.Location.Address))
            return ServiceResult<IncidentDetailDto>.Invalid("missing_location", "A location address or description is required.");
        if (request.SourceCallId == Guid.Empty)
            return ServiceResult<IncidentDetailDto>.Invalid("invalid_source_call", "SourceCallId cannot be an empty GUID.");
        if (request.SourceCallId is Guid sourceId &&
            await db.Incidents.AnyAsync(i => i.SourceCallId == sourceId, ct))
            return ServiceResult<IncidentDetailDto>.Conflict("duplicate_source_call", "An incident already exists for this source call.");

        var now = DateTime.UtcNow;
        var incident = new Incident
        {
            Type = request.Type.Value, Severity = request.Severity.Value, Status = IncidentStatus.Reported,
            Location = new Location { Latitude = request.Location.Latitude, Longitude = request.Location.Longitude,
                Address = request.Location.Address.Trim() },
            Description = request.Description?.Trim(), CallerName = request.CallerName?.Trim(),
            CallerPhone = request.CallerPhone?.Trim(), SourceCallId = request.SourceCallId,
            ReportedAt = now, CreatedBy = userId
        };
        db.Incidents.Add(incident);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        { return ServiceResult<IncidentDetailDto>.Conflict("duplicate_source_call", "An incident already exists for this source call."); }
        return ServiceResult<IncidentDetailDto>.Created(Detail(incident));
    }

    public async Task<ServiceResult<IncidentDetailDto>> ChangeStatusAsync(
        Guid id, ChangeIncidentStatusRequest request, Guid userId, CancellationToken ct)
    {
        var incident = await db.Incidents.Include(i => i.Assignments).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (incident is null) return ServiceResult<IncidentDetailDto>.Missing("incident_not_found", "Incident not found.");
        if (!request.Status.HasValue || !Enum.IsDefined(request.Status.Value))
            return ServiceResult<IncidentDetailDto>.Invalid("invalid_status", "Unknown incident status.");
        if (request.Status.Value == IncidentStatus.Closed)
            return ServiceResult<IncidentDetailDto>.Conflict("report_required", "File an after-action report to close the incident.");
        // Enforce ordered status changes. AssignAsync owns the Reported -> Dispatched transition.
        bool allowed = (incident.Status, request.Status.Value) switch
        {
            (IncidentStatus.Dispatched, IncidentStatus.EnRoute) => true,
            (IncidentStatus.EnRoute, IncidentStatus.OnScene) => true,
            (IncidentStatus.OnScene, IncidentStatus.Resolved) => true,
            _ => false
        };
        if (!allowed) return ServiceResult<IncidentDetailDto>.Conflict("invalid_status_transition", "Invalid incident status transition.");
        var now = DateTime.UtcNow;
        var previous = incident.Status;
        incident.Status = request.Status.Value;
        incident.UpdatedBy = userId;
        db.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = id, FromStatus = previous, ToStatus = request.Status.Value,
            ChangedAt = now, ChangedBy = userId, CreatedBy = userId
        });
        if (request.Status.Value == IncidentStatus.OnScene)
        {
            foreach (var a in incident.Assignments.Where(a => a.ReleasedAt is null))
            {
                if (!a.ArrivedAt.HasValue) a.ArrivedAt = now;
                var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == a.UnitId, ct);
                if (unit is not null) { unit.Status = UnitStatus.OnScene; unit.UpdatedBy = userId; }
            }
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { return ServiceResult<IncidentDetailDto>.Conflict("incident_changed", "Incident changed concurrently."); }
        return ServiceResult<IncidentDetailDto>.Ok((await GetAsync(id, ct))!);
    }

    public async Task<ServiceResult<IncidentDetailDto>> FileReportAsync(
        Guid id, FileAfterActionReportRequest request, Guid userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Outcome) || request.Injuries < 0 || request.Deaths < 0
            || request.HydrantWaterLitres < 0 || double.IsNaN(request.HydrantWaterLitres)
            || double.IsInfinity(request.HydrantWaterLitres))
            return ServiceResult<IncidentDetailDto>.Invalid("invalid_report", "A valid outcome and non-negative report counts are required.");

        var incident = await db.Incidents.Include(i => i.Report).Include(i => i.Assignments)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (incident is null) return ServiceResult<IncidentDetailDto>.Missing("incident_not_found", "Incident not found.");
        if (incident.Status != IncidentStatus.Resolved)
            return ServiceResult<IncidentDetailDto>.Conflict("incident_not_resolved", "Incident must be Resolved before closing.");
        if (incident.Report is not null)
            return ServiceResult<IncidentDetailDto>.Conflict("report_exists", "An after-action report already exists.");
        // Close the incident and release its units together. Assignments are retained as history.
        var now = DateTime.UtcNow;
        foreach (var assignment in incident.Assignments.Where(a => a.ReleasedAt is null))
        {
            var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == assignment.UnitId, ct);
            if (unit is null)
                return ServiceResult<IncidentDetailDto>.Conflict("unit_missing", "Assigned unit no longer exists.");
            assignment.ReleasedAt = now;
            unit.Status = UnitStatus.Available;
            unit.UpdatedBy = userId;
        }
        var report = new AfterActionReport
        {
            IncidentId = id, Outcome = request.Outcome.Trim(), Injuries = request.Injuries,
            Deaths = request.Deaths, HydrantWaterLitres = request.HydrantWaterLitres,
            Notes = request.Notes?.Trim(), FiledBy = userId, FiledAt = now, CreatedBy = userId
        };
        incident.Status = IncidentStatus.Closed;
        incident.ClosedAt = now;
        incident.UpdatedBy = userId;
        db.AfterActionReports.Add(report);
        db.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = id, FromStatus = IncidentStatus.Resolved, ToStatus = IncidentStatus.Closed,
            ChangedBy = userId, ChangedAt = now, CreatedBy = userId
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { return ServiceResult<IncidentDetailDto>.Conflict("incident_changed", "Incident changed concurrently."); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        { return ServiceResult<IncidentDetailDto>.Conflict("report_exists", "Report already exists."); }
        // ES-FR-016: queue hydrant notice to Maintenance via a durable outbox when contract exists.
        return ServiceResult<IncidentDetailDto>.Ok((await GetAsync(id, ct))!);
    }

    private static IncidentSummaryDto Summarize(Incident i) => new(
        i.Id, i.Type.ToString(), (int)i.Severity, i.Status.ToString(),
        i.Location.Latitude, i.Location.Longitude, i.ReportedAt);

    private static IncidentDetailDto Detail(Incident i) => new(
        i.Id, i.Type.ToString(), (int)i.Severity, i.Status.ToString(),
        new IncidentLocationRequest(i.Location.Latitude, i.Location.Longitude, i.Location.Address),
        i.Description, i.CallerName, i.CallerPhone, i.SourceCallId,
        i.ReportedAt, i.ClosedAt,
        i.StatusHistory.OrderBy(x => x.ChangedAt)
            .Select(x => new IncidentStatusHistoryDto(x.FromStatus.ToString(), x.ToStatus.ToString(), x.ChangedBy, x.ChangedAt)).ToList(),
        i.Assignments.OrderBy(a => a.AssignedAt)
            .Select(a => new AssignmentDto(a.Id, a.IncidentId, a.UnitId, a.AssignedAt, a.ArrivedAt, a.ReleasedAt)).ToList(),
        i.Report is null ? null : new AfterActionReportDto(i.Report.Outcome, i.Report.Injuries,
            i.Report.Deaths, i.Report.HydrantWaterLitres, i.Report.Notes, i.Report.FiledBy, i.Report.FiledAt));

    private static bool CoordinatesValid(double lat, double lon) =>
        double.IsFinite(lat) && double.IsFinite(lon) && lat is >= -90 and <= 90 && lon is >= -180 and <= 180;

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Number is 2601 or 2627;
}
