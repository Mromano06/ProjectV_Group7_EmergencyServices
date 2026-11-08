using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Clients;
using MyApplication.Api.Data;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Services;

public class WeatherWarningService : IWeatherWarningService
{
    private readonly EmergencyDbContext _db;
    private readonly ITransitWarningClient _transit;
    private readonly ILogger<WeatherWarningService> _logger;

    public WeatherWarningService(
        EmergencyDbContext db,
        ITransitWarningClient transit,
        ILogger<WeatherWarningService> logger)
    {
        _db = db;
        _transit = transit;
        _logger = logger;
    }

    //Reading

    public async Task<List<WeatherWarning>> GetActiveAsync()
    {
        var now = DateTime.UtcNow;

        // "Active" = started already and not ended yet. Because this is checked against
        // the clock every time, warnings drop off automatically when EndsAt passes.
        return await _db.WeatherWarnings
            .Include(w => w.IncidentLinks)
            .Where(w => w.StartsAt <= now && w.EndsAt > now)
            .OrderBy(w => w.EndsAt)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<PagedResult<WeatherWarning>> GetHistoryAsync(int page, int pageSize)
    {
        // Keep page numbers sensible (SH-I05: default 25 per page).
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 25;

        var query = _db.WeatherWarnings.AsNoTracking();
        var total = await query.CountAsync();

        var items = await query
            .Include(w => w.IncidentLinks)
            .OrderByDescending(w => w.StartsAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<WeatherWarning>(items, page, pageSize, total);
    }

    public async Task<WeatherWarning?> GetByIdAsync(Guid id)
    {
        return await _db.WeatherWarnings
            .Include(w => w.IncidentLinks)
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id);
    }

    //Admin actions

    public async Task<WarningResult> CreateAsync(CreateWeatherWarningRequest request, Guid userId, WarningSource source = WarningSource.Manual)
    {
        var now = DateTime.UtcNow;
        var startsAt = ToUtc(request.StartsAt ?? now);
        var endsAt = ToUtc(request.EndsAt);

        var problem = Validate(request, startsAt, endsAt, now);
        if (problem != null) return problem;

        var warning = new WeatherWarning
        {
            Type = request.Type.Trim(),
            Severity = NormalizeSeverity(request.Severity),
            Area = request.Area.Trim(),
            Description = request.Description?.Trim(),
            StartsAt = startsAt,
            EndsAt = endsAt,
            Source = source,
            AffectsTransit = request.AffectsTransit,
            CreatedBy = userId
        };

        _db.WeatherWarnings.Add(warning);
        await _db.SaveChangesAsync();

        _logger.LogInformation("{Source} warning {Id} ({Type}, {Area}) issued by {User}",
            source, warning.Id, warning.Type, warning.Area, userId);

        if (warning.AffectsTransit)
        {
            await TrySendToTransitAsync(warning);
        }

        return WarningResult.Ok(warning);
    }

    public async Task<WarningResult> UpdateAsync(Guid id, UpdateWeatherWarningRequest request, Guid userId)
    {
        var now = DateTime.UtcNow;
        var warning = await _db.WeatherWarnings.Include(w => w.IncidentLinks).FirstOrDefaultAsync(w => w.Id == id);
        if (warning == null) return WarningResult.NotFound(id);

        // Board story says "update or extend an ACTIVE warning", so ended ones are locked.
        // also keeps the history of past warnings honest.
        if (warning.GetStatus(now) == WarningStatus.Ended)
        {
            return WarningResult.Conflict("warning_ended", "This warning has already ended and can't be changed. Issue a new warning instead.");
        }

        var startsAt = ToUtc(request.StartsAt ?? warning.StartsAt);
        var endsAt = ToUtc(request.EndsAt);

        var problem = Validate(request, startsAt, endsAt, now);
        if (problem != null) return problem;

        var wasTransitRelevant = warning.AffectsTransit;

        warning.Type = request.Type.Trim();
        warning.Severity = NormalizeSeverity(request.Severity);
        warning.Area = request.Area.Trim();
        warning.Description = request.Description?.Trim();
        warning.StartsAt = startsAt;
        warning.EndsAt = endsAt;
        warning.AffectsTransit = request.AffectsTransit;
        warning.UpdatedBy = userId;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Warning {Id} updated by {User}", warning.Id, userId);

        // Tell Transit if it affects them now, or used to (so they can clear it).
        if (warning.AffectsTransit || wasTransitRelevant)
        {
            await TrySendToTransitAsync(warning);
        }

        return WarningResult.Ok(warning);
    }

    public async Task<WarningResult> ExpireAsync(Guid id, Guid userId)
    {
        var now = DateTime.UtcNow;
        var warning = await _db.WeatherWarnings.Include(w => w.IncidentLinks).FirstOrDefaultAsync(w => w.Id == id);
        if (warning == null) return WarningResult.NotFound(id);

        if (warning.GetStatus(now) == WarningStatus.Ended)
        {
            return WarningResult.Conflict("warning_already_ended", "This warning has already ended.");
        }

        // A scheduled warning that hasn't started yet is "cancelled": end it at its start time
        // so it never shows as active. An active one ends right now.
        if (warning.StartsAt > now)
        {
            warning.EndsAt = warning.StartsAt;
        }
        else
        {
            warning.Expire(now);
        }
        warning.UpdatedBy = userId;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Warning {Id} ended early by {User}", warning.Id, userId);

        if (warning.AffectsTransit)
        {
            await TrySendToTransitAsync(warning);
        }

        return WarningResult.Ok(warning);
    }

    public async Task<(WarningResult Result, bool Sent)> NotifyTransitAsync(Guid id)
    {
        var warning = await _db.WeatherWarnings.Include(w => w.IncidentLinks).FirstOrDefaultAsync(w => w.Id == id);
        if (warning == null) return (WarningResult.NotFound(id), false);

        var sent = await TrySendToTransitAsync(warning);
        return (WarningResult.Ok(warning), sent);
    }

    public async Task<WarningResult> LinkIncidentAsync(Guid id, Guid incidentId, Guid userId)
    {
        if (incidentId == Guid.Empty)
        {
            return WarningResult.Invalid("invalid_incident_id", "incidentId is required.");
        }

        var warning = await _db.WeatherWarnings.Include(w => w.IncidentLinks).FirstOrDefaultAsync(w => w.Id == id);
        if (warning == null) return WarningResult.NotFound(id);

        if (warning.IncidentLinks.Any(l => l.IncidentId == incidentId))
        {
            return WarningResult.Conflict("already_linked", "That incident is already linked to this warning.");
        }

        // TODO: once Incident is in EmergencyDbContext, check the incident exists:
        // if (!await _db.Incidents.AnyAsync(i => i.Id == incidentId))
        //     return WarningResult.Invalid("unknown_incident", $"No incident with id {incidentId}.");

        warning.IncidentLinks.Add(new WarningIncidentLink
        {
            WeatherWarningId = warning.Id,
            IncidentId = incidentId,
            CreatedBy = userId
        });

        await _db.SaveChangesAsync();
        return WarningResult.Ok(warning);
    }

    //Helpers

    /// <summary>Checks the rules that [Required] attributes can't check on their own.</summary>
    private static WarningResult? Validate(CreateWeatherWarningRequest request, DateTime startsAt, DateTime endsAt, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
            return WarningResult.Invalid("missing_type", "Type is required.");

        if (string.IsNullOrWhiteSpace(request.Area))
            return WarningResult.Invalid("missing_area", "Area is required.");

        if (NormalizeSeverity(request.Severity) == string.Empty)
            return WarningResult.Invalid("invalid_severity",
                $"Severity must be one of: {string.Join(", ", WeatherWarning.AllowedSeverities)}.");

        if (endsAt <= startsAt)
            return WarningResult.Invalid("invalid_time_window", "EndsAt must be after StartsAt.");

        if (endsAt <= now)
            return WarningResult.Invalid("ends_in_past", "EndsAt must be in the future.");

        return null;
    }

    /// <summary>Turns "watch" or "WATCH" into "Watch". Returns "" if it isn't an allowed value.</summary>
    private static string NormalizeSeverity(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity)) return string.Empty;
        return WeatherWarning.AllowedSeverities
            .FirstOrDefault(s => s.Equals(severity.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    /// <summary>All dates are stored in UTC (SH-I04).</summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc) // no timezone given: assume it was already UTC
    };

    /// <summary>
    /// Sends the warning to Public Transport. If Transit is down we log it and carry on –
    /// a broken Transit service must not stop us issuing warnings (SH-A07).
    /// </summary>
    private async Task<bool> TrySendToTransitAsync(WeatherWarning warning)
    {
        var notice = new TransitWarningNotice(
            warning.Id,
            warning.Type,
            warning.Severity,
            warning.Area,
            warning.StartsAt,
            warning.EndsAt,
            warning.GetStatus(DateTime.UtcNow).ToString());

        var sent = await _transit.SendWarningNoticeAsync(notice);
        if (sent)
        {
            warning.LastSentToTransitAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return sent;
    }
}
