using MyApplication.Api.Data;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MyApplication.Api.Services;

/// <summary>
/// Settings for the simulator, read from the "WarningSimulator" section of appsettings.json.
/// </summary>
public class WarningSimulatorOptions
{
    /// <summary>Turn the whole simulator on or off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often it wakes up to (maybe) make a warning.</summary>
    public int IntervalMinutes { get; set; } = 10;

    /// <summary>Chance (0.0 – 1.0) of making a new random warning each time it wakes up.</summary>
    public double ChanceOfNewWarning { get; set; } = 0.25;

    /// <summary>If true and nothing is active, it always makes one – so the banner is never empty for a demo.</summary>
    public bool KeepAtLeastOneActive { get; set; } = true;

    /// <summary>Never let random warnings pile up past this many active at once.</summary>
    public int MaxActiveSimulated { get; set; } = 3;
}

/// <summary>
/// Makes up random, fake weather and public warnings in the background.
/// We use this INSTEAD of pulling real alerts from Environment Canada (ES-FR-023),
/// It runs inside the API on a timer. Every warning it makes is marked
/// Source = Simulated and goes through the normal WeatherWarningService, so it shows up
/// in the banner, the incident board and Transit notices exactly like a real one –
/// and it still ends automatically when its EndsAt passes.
/// </summary>
public class SimulatedWarningGenerator : BackgroundService
{
    //The made-up data to pick from

    private record WarningTemplate(string Type, string Description, bool AffectsTransit, int MinHours, int MaxHours);

    private static readonly WarningTemplate[] Templates =
    {
        // Weather
        new("Snowfall",            "Heavy snow expected. Roads and sidewalks may be slippery.",            true,  4, 18),
        new("Freezing Rain",       "Ice build-up on roads, sidewalks and power lines.",                    true,  3, 12),
        new("Extreme Cold",        "Very cold wind chills. Limit time outdoors and check on neighbours.",  false, 8, 36),
        new("Heat",                "High temperatures and humidity. Stay hydrated and seek shade.",        false, 8, 36),
        new("Severe Thunderstorm", "Strong winds, heavy rain and possible hail.",                          true,  1, 6),
        new("Wind",                "Strong gusts may cause damage and power outages.",                     true,  3, 12),
        new("Fog",                 "Near-zero visibility in dense fog. Drive with care.",                  true,  2, 8),
        new("Flood",               "Rising water levels near rivers and low-lying areas.",                 true,  6, 48),
        // Public
        new("Air Quality",         "Poor air quality. People with breathing conditions should stay inside.", false, 6, 24),
        new("Boil Water Advisory", "Boil tap water for one minute before drinking or cooking.",            false, 12, 72),
        new("Power Outage",        "Widespread power outage. Traffic signals may be out.",                 true,  1, 8),
        new("Gas Leak",            "Gas leak reported. Avoid the area while crews respond.",               true,  1, 4),
    };

    private static readonly string[] Areas =
    {
        "City-wide", "Downtown", "North End", "South End", "East End", "West End", "Industrial Park", "Riverside"
    };

    // Picked with weights so mild ones show up more often than serious ones.
    private static readonly (string Severity, int Weight)[] Severities =
    {
        ("Statement", 3), ("Advisory", 4), ("Watch", 2), ("Warning", 1)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WarningSimulatorOptions _options;
    private readonly ILogger<SimulatedWarningGenerator> _logger;

    public SimulatedWarningGenerator(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<SimulatedWarningGenerator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = config.GetSection("WarningSimulator").Get<WarningSimulatorOptions>() ?? new WarningSimulatorOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Warning simulator is turned off.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        _logger.LogInformation("Warning simulator running every {Minutes} min.", interval.TotalMinutes);

        // Small wait so the database is ready when the app first starts.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex)
            {
                // Never let the simulator crash the API (e.g. database not up yet).
                _logger.LogWarning(ex, "Warning simulator tick failed; will try again next time.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    /// <summary>One "wake up": decide whether to make a warning.</summary>
    private async Task TickAsync()
    {
        // BackgroundServices live for the whole app, but the database/service are per-request,
        // so we open a short "scope" to borrow them.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmergencyDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IWeatherWarningService>();

        var now = DateTime.UtcNow;
        var activeCount = await db.WeatherWarnings.CountAsync(w => w.StartsAt <= now && w.EndsAt > now);
        var activeSimulated = await db.WeatherWarnings.CountAsync(w =>
            w.Source == WarningSource.Simulated && w.StartsAt <= now && w.EndsAt > now);

        var mustMakeOne = _options.KeepAtLeastOneActive && activeCount == 0;
        var rollSaysYes = Random.Shared.NextDouble() < _options.ChanceOfNewWarning;

        if (!mustMakeOne && (!rollSaysYes || activeSimulated >= _options.MaxActiveSimulated))
        {
            return;
        }

        await CreateRandomWarningAsync(service);
    }

    /// <summary>
    /// Builds one random warning and saves it. Public so the controller's
    /// "simulate now" endpoint can reuse it during a demo.
    /// </summary>
    public static async Task<WarningResult> CreateRandomWarningAsync(IWeatherWarningService service)
    {
        var template = Templates[Random.Shared.Next(Templates.Length)];
        var area = Areas[Random.Shared.Next(Areas.Length)];
        var hours = Random.Shared.Next(template.MinHours, template.MaxHours + 1);

        var request = new CreateWeatherWarningRequest
        {
            Type = template.Type,
            Severity = PickSeverity(),
            Area = area,
            Description = $"[SIMULATED] {template.Description}",
            StartsAt = DateTime.UtcNow,                 // starts right away
            EndsAt = DateTime.UtcNow.AddHours(hours),   // then ends on its own
            AffectsTransit = template.AffectsTransit
        };

        // Guid.Empty as the creator = "the system", not a person.
        return await service.CreateAsync(request, Guid.Empty, WarningSource.Simulated);
    }

    private static string PickSeverity()
    {
        var total = Severities.Sum(s => s.Weight);
        var roll = Random.Shared.Next(total);
        foreach (var (severity, weight) in Severities)
        {
            if (roll < weight) return severity;
            roll -= weight;
        }
        return "Advisory";
    }
}
