using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Data;
using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;

namespace MyApplication.Api.Services;

// Matthew Romano - Nov 14th, 2026 - Stats Service

/// <summary>ES-FR-024/027. Date range uses [from, to) in UTC, based on ReportedAt.</summary>
public sealed class StatsService(EmergencyDbContext db) : IStatsService
{
    // Gets all exisitng stats from 1 timestamp to another
    public async Task<ServiceResult<StatsDto>> GetStatsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        if (from >= to) return ServiceResult<StatsDto>.Invalid("invalid_date_range", "'to' must be after 'from'.");
        var incidents = await db.Incidents.AsNoTracking()
            .Where(i => i.ReportedAt >= from && i.ReportedAt < to)
            .Select(i => new { i.Id, i.Type, i.Status, i.ReportedAt }).ToListAsync(ct);
        var byType = incidents.GroupBy(i => i.Type.ToString()).ToDictionary(g => g.Key, g => g.Count());
        var byStatus = incidents.GroupBy(i => i.Status.ToString()).ToDictionary(g => g.Key, g => g.Count());

        // Response time is ReportedAt -> FIRST transition to OnScene (per SRS), not time to dispatch.
        var arrivalRows = await (
            from h in db.IncidentStatusHistories.AsNoTracking()
            join i in db.Incidents.AsNoTracking() on h.IncidentId equals i.Id
            where i.ReportedAt >= @from && i.ReportedAt < to && h.ToStatus == IncidentStatus.OnScene            select new { h.IncidentId, i.ReportedAt, h.ChangedAt }).ToListAsync(ct);
        var durations = arrivalRows.GroupBy(r => r.IncidentId).Select(group =>
        {
            var first = group.Min(r => r.ChangedAt);
            return (first - group.First().ReportedAt).TotalMinutes;
        }).Where(minutes => minutes >= 0).ToList();

        var deathCount = await (
            from r in db.AfterActionReports.AsNoTracking()
            join i in db.Incidents.AsNoTracking() on r.IncidentId equals i.Id
            where i.ReportedAt >= @from && i.ReportedAt < to
            select (int?)r.Deaths).SumAsync(ct) ?? 0;

        return ServiceResult<StatsDto>.Ok(new StatsDto(from, to, incidents.Count,
            byType, byStatus, durations.Count == 0 ? null : durations.Average(), deathCount));
    }

    // Exports the incident repots as a CSV
    public async Task<ServiceResult<string>> ExportCsvAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        if (from >= to) return ServiceResult<string>.Invalid("invalid_date_range", "'to' must be after 'from'.");
        // Intentionally EXCLUDES caller personal information to avoid exposing restricted fields.
        var incidents = await db.Incidents.AsNoTracking()
            .Where(i => i.ReportedAt >= from && i.ReportedAt < to)
            .OrderBy(i => i.ReportedAt).ToListAsync(ct);
        var csv = new StringBuilder("Id,Type,Severity,Status,ReportedAt,ClosedAt,Latitude,Longitude\r\n");
        foreach (var i in incidents)
        {
            // These cells are IDs/enums/numbers/timestamps, not user-supplied text (CSV injection protection).
            csv.Append(string.Join(',',
                i.Id.ToString(), i.Type.ToString(), i.Severity.ToString(), i.Status.ToString(),
                i.ReportedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                i.ClosedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "",
                i.Location.Latitude.ToString(CultureInfo.InvariantCulture),
                i.Location.Longitude.ToString(CultureInfo.InvariantCulture)));
            csv.Append("\r\n");
        }
        return ServiceResult<string>.Ok(csv.ToString());
    }
}
