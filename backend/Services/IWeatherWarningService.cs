using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Services;

/// <summary>
/// All the warning logic (class diagram: IWeatherWarningService).
/// The controller only deals with HTTP; this does the actual work.
/// </summary>
public interface IWeatherWarningService
{
    /// <summary>Warnings in effect right now – used by the app banner, other services and the incident board.</summary>
    Task<List<WeatherWarning>> GetActiveAsync();

    /// <summary>Current and past warnings, newest first, one page at a time.</summary>
    Task<PagedResult<WeatherWarning>> GetHistoryAsync(int page, int pageSize);

    Task<WeatherWarning?> GetByIdAsync(Guid id);

    /// <summary>
    /// Issue a new warning. Admins use the default (Manual);
    /// the SimulatedWarningGenerator passes WarningSource.Simulated.
    /// </summary>
    Task<WarningResult> CreateAsync(CreateWeatherWarningRequest request, Guid userId, WarningSource source = WarningSource.Manual);

    /// <summary>Admin updates or extends a warning that hasn't ended yet.</summary>
    Task<WarningResult> UpdateAsync(Guid id, UpdateWeatherWarningRequest request, Guid userId);

    /// <summary>Admin ends a warning early.</summary>
    Task<WarningResult> ExpireAsync(Guid id, Guid userId);

    /// <summary>Admin (re)sends a warning to Public Transport. Returns true if Transit accepted it.</summary>
    Task<(WarningResult Result, bool Sent)> NotifyTransitAsync(Guid id);

    /// <summary>Link an incident to a warning.</summary>
    Task<WarningResult> LinkIncidentAsync(Guid id, Guid incidentId, Guid userId);
}
