using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyApplication.Api.Clients;
using MyApplication.Api.Data;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;
using MyApplication.Api.Services;

namespace MyApplication.Api.Tests;

/// <summary>
/// A pretend Public Transport service. Records what we sent and
/// can be told to "fail" so we can test what happens when Transit is down.
/// </summary>
public class FakeTransitClient : ITransitWarningClient
{
    public bool ShouldSucceed { get; set; } = true;
    public List<TransitWarningNotice> Sent { get; } = new();

    public Task<bool> SendWarningNoticeAsync(TransitWarningNotice notice)
    {
        Sent.Add(notice);
        return Task.FromResult(ShouldSucceed);
    }
}

/// <summary>
/// Tests for WeatherWarningService – the actual warning rules.
/// Each test gets its own brand-new in-memory database, so tests can't affect each other.
/// </summary>
public class WeatherWarningServiceTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly EmergencyDbContext _db;
    private readonly FakeTransitClient _transit;
    private readonly WeatherWarningService _service;

    public WeatherWarningServiceTests()
    {
        var options = new DbContextOptionsBuilder<EmergencyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new EmergencyDbContext(options);
        _transit = new FakeTransitClient();
        _service = new WeatherWarningService(_db, _transit, NullLogger<WeatherWarningService>.Instance);
    }

    /// <summary>A request that passes all the rules. Tests change one thing to break it.</summary>
    private static CreateWeatherWarningRequest ValidRequest(bool affectsTransit = false) => new()
    {
        Type = "Snowfall",
        Severity = "Warning",
        Area = "City-wide",
        Description = "Heavy snow.",
        EndsAt = DateTime.UtcNow.AddHours(6),
        AffectsTransit = affectsTransit
    };

    private static UpdateWeatherWarningRequest UpdateRequest(DateTime endsAt) => new()
    {
        Type = "Snowfall",
        Severity = "Warning",
        Area = "City-wide",
        EndsAt = endsAt
    };

    // ---------- Issue (ES-FR-021) ----------

    // TC-WW-07
    [Fact]
    public async Task Create_ValidWarning_IsSavedAsActiveManualWarning()
    {
        var result = await _service.CreateAsync(ValidRequest(), AdminId);

        Assert.Equal(WarningOutcome.Ok, result.Outcome);
        var saved = await _db.WeatherWarnings.SingleAsync();
        Assert.Equal("Snowfall", saved.Type);
        Assert.Equal(WarningSource.Manual, saved.Source);
        Assert.Equal(AdminId, saved.CreatedBy);
        Assert.NotEqual(default, saved.CreatedAt);              // audit field filled in (SH-D04)
        Assert.Equal(WarningStatus.Active, saved.GetStatus(DateTime.UtcNow));
    }

    // TC-WW-08
    [Fact]
    public async Task Create_WithUnknownSeverity_IsRejected()
    {
        var request = ValidRequest();
        request.Severity = "Scary";

        var result = await _service.CreateAsync(request, AdminId);

        Assert.Equal(WarningOutcome.Invalid, result.Outcome);
        Assert.Equal("invalid_severity", result.ErrorCode);
        Assert.Empty(_db.WeatherWarnings);
    }

    // TC-WW-09
    [Fact]
    public async Task Create_WithEndBeforeStart_IsRejected()
    {
        var request = ValidRequest();
        request.StartsAt = DateTime.UtcNow.AddHours(5);
        request.EndsAt = DateTime.UtcNow.AddHours(2);

        var result = await _service.CreateAsync(request, AdminId);

        Assert.Equal(WarningOutcome.Invalid, result.Outcome);
        Assert.Equal("invalid_time_window", result.ErrorCode);
    }

    // TC-WW-10
    [Fact]
    public async Task Create_EndingInThePast_IsRejected()
    {
        var request = ValidRequest();
        request.StartsAt = DateTime.UtcNow.AddHours(-5);
        request.EndsAt = DateTime.UtcNow.AddHours(-1);

        var result = await _service.CreateAsync(request, AdminId);

        Assert.Equal(WarningOutcome.Invalid, result.Outcome);
        Assert.Equal("ends_in_past", result.ErrorCode);
    }

    // TC-WW-11
    [Fact]
    public async Task Create_SeverityIsCaseInsensitive_AndStoredNeatly()
    {
        var request = ValidRequest();
        request.Severity = "wATcH";

        var result = await _service.CreateAsync(request, AdminId);

        Assert.Equal(WarningOutcome.Ok, result.Outcome);
        Assert.Equal("Watch", result.Warning!.Severity);
    }

    // ---------- Read (ES-FR-022, SH-C13) ----------

    // TC-WW-12
    [Fact]
    public async Task GetActive_ReturnsOnlyWarningsInEffectRightNow()
    {
        var now = DateTime.UtcNow;
        _db.WeatherWarnings.AddRange(
            new WeatherWarning { Type = "Active", Severity = "Warning", Area = "A", StartsAt = now.AddHours(-1), EndsAt = now.AddHours(1) },
            new WeatherWarning { Type = "Ended", Severity = "Warning", Area = "A", StartsAt = now.AddHours(-5), EndsAt = now.AddHours(-1) },
            new WeatherWarning { Type = "Scheduled", Severity = "Warning", Area = "A", StartsAt = now.AddHours(2), EndsAt = now.AddHours(5) });
        await _db.SaveChangesAsync();

        var active = await _service.GetActiveAsync();

        var only = Assert.Single(active);
        Assert.Equal("Active", only.Type);
    }

    // TC-WW-13
    [Fact]
    public async Task GetHistory_ReturnsCurrentAndPastWarnings_NewestFirst_InPages()
    {
        var now = DateTime.UtcNow;
        _db.WeatherWarnings.AddRange(
            new WeatherWarning { Type = "Oldest", Severity = "Advisory", Area = "A", StartsAt = now.AddDays(-3), EndsAt = now.AddDays(-2) },
            new WeatherWarning { Type = "Middle", Severity = "Advisory", Area = "A", StartsAt = now.AddDays(-2), EndsAt = now.AddDays(-1) },
            new WeatherWarning { Type = "Newest", Severity = "Advisory", Area = "A", StartsAt = now.AddHours(-1), EndsAt = now.AddHours(3) });
        await _db.SaveChangesAsync();

        var page1 = await _service.GetHistoryAsync(page: 1, pageSize: 2);
        var page2 = await _service.GetHistoryAsync(page: 2, pageSize: 2);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(new[] { "Newest", "Middle" }, page1.Items.Select(w => w.Type));
        Assert.Equal("Oldest", Assert.Single(page2.Items).Type);
    }

    // ---------- Update / extend ----------

    // TC-WW-14
    [Fact]
    public async Task Update_ExtendsAnActiveWarning()
    {
        var created = await _service.CreateAsync(ValidRequest(), AdminId);
        var newEnd = DateTime.UtcNow.AddHours(24);

        var result = await _service.UpdateAsync(created.Warning!.Id, UpdateRequest(newEnd), AdminId);

        Assert.Equal(WarningOutcome.Ok, result.Outcome);
        Assert.Equal(newEnd, result.Warning!.EndsAt);
        Assert.Equal(AdminId, result.Warning.UpdatedBy);
        Assert.NotNull(result.Warning.UpdatedAt);
    }

    // TC-WW-15
    [Fact]
    public async Task Update_AWarningThatHasEnded_IsAConflict()
    {
        var created = await _service.CreateAsync(ValidRequest(), AdminId);
        await _service.ExpireAsync(created.Warning!.Id, AdminId);

        var result = await _service.UpdateAsync(created.Warning.Id, UpdateRequest(DateTime.UtcNow.AddHours(5)), AdminId);

        Assert.Equal(WarningOutcome.Conflict, result.Outcome);
        Assert.Equal("warning_ended", result.ErrorCode);
    }

    // TC-WW-16
    [Fact]
    public async Task Update_UnknownWarning_IsNotFound()
    {
        var result = await _service.UpdateAsync(Guid.NewGuid(), UpdateRequest(DateTime.UtcNow.AddHours(5)), AdminId);

        Assert.Equal(WarningOutcome.NotFound, result.Outcome);
    }

    // ---------- End early ----------

    // TC-WW-17
    [Fact]
    public async Task Expire_EndsAnActiveWarningEarly_AndItLeavesTheActiveList()
    {
        var created = await _service.CreateAsync(ValidRequest(), AdminId);

        var result = await _service.ExpireAsync(created.Warning!.Id, AdminId);

        Assert.Equal(WarningOutcome.Ok, result.Outcome);
        Assert.Empty(await _service.GetActiveAsync());
        Assert.Single(await _db.WeatherWarnings.ToListAsync()); // kept for history, not deleted
    }

    // TC-WW-18
    [Fact]
    public async Task Expire_Twice_IsAConflict()
    {
        var created = await _service.CreateAsync(ValidRequest(), AdminId);
        await _service.ExpireAsync(created.Warning!.Id, AdminId);

        var result = await _service.ExpireAsync(created.Warning.Id, AdminId);

        Assert.Equal(WarningOutcome.Conflict, result.Outcome);
        Assert.Equal("warning_already_ended", result.ErrorCode);
    }

    // TC-WW-19
    [Fact]
    public async Task Expire_AScheduledWarning_CancelsItSoItNeverBecomesActive()
    {
        var request = ValidRequest();
        request.StartsAt = DateTime.UtcNow.AddHours(2);
        request.EndsAt = DateTime.UtcNow.AddHours(6);
        var created = await _service.CreateAsync(request, AdminId);

        await _service.ExpireAsync(created.Warning!.Id, AdminId);

        var warning = await _db.WeatherWarnings.SingleAsync();
        Assert.Equal(warning.StartsAt, warning.EndsAt);
        Assert.False(warning.IsActive(warning.StartsAt.AddMinutes(1)));
    }

    // ---------- Public Transport ----------

    // TC-WW-20
    [Fact]
    public async Task Create_TransitAffectingWarning_IsSentToPublicTransport()
    {
        var result = await _service.CreateAsync(ValidRequest(affectsTransit: true), AdminId);

        var notice = Assert.Single(_transit.Sent);
        Assert.Equal(result.Warning!.Id, notice.WarningId);
        Assert.NotNull(result.Warning.LastSentToTransitAt);
    }

    // TC-WW-21
    [Fact]
    public async Task Create_WarningThatDoesNotAffectTransit_IsNotSent()
    {
        await _service.CreateAsync(ValidRequest(affectsTransit: false), AdminId);

        Assert.Empty(_transit.Sent);
    }

    // TC-WW-22
    [Fact]
    public async Task Create_StillSucceeds_WhenPublicTransportIsDown()
    {
        _transit.ShouldSucceed = false;

        var result = await _service.CreateAsync(ValidRequest(affectsTransit: true), AdminId);

        Assert.Equal(WarningOutcome.Ok, result.Outcome);
        Assert.Single(await _db.WeatherWarnings.ToListAsync());
        Assert.Null(result.Warning!.LastSentToTransitAt);
    }

    // ---------- Link incidents ----------

    // TC-WW-23
    [Fact]
    public async Task LinkIncident_AddsTheIncidentToTheWarning()
    {
        var created = await _service.CreateAsync(ValidRequest(), AdminId);
        var incidentId = Guid.NewGuid();

        var result = await _service.LinkIncidentAsync(created.Warning!.Id, incidentId, AdminId);

        Assert.Equal(WarningOutcome.Ok, result.Outcome);
        Assert.Contains(result.Warning!.IncidentLinks, l => l.IncidentId == incidentId);
    }

    // TC-WW-24
    [Fact]
    public async Task LinkIncident_SameIncidentTwice_IsAConflict()
    {
        var created = await _service.CreateAsync(ValidRequest(), AdminId);
        var incidentId = Guid.NewGuid();
        await _service.LinkIncidentAsync(created.Warning!.Id, incidentId, AdminId);

        var result = await _service.LinkIncidentAsync(created.Warning.Id, incidentId, AdminId);

        Assert.Equal(WarningOutcome.Conflict, result.Outcome);
        Assert.Equal("already_linked", result.ErrorCode);
    }

    // TC-WW-25
    [Fact]
    public async Task LinkIncident_WithEmptyIncidentId_IsRejected()
    {
        var created = await _service.CreateAsync(ValidRequest(), AdminId);

        var result = await _service.LinkIncidentAsync(created.Warning!.Id, Guid.Empty, AdminId);

        Assert.Equal(WarningOutcome.Invalid, result.Outcome);
    }

    // ---------- Simulator ----------

    // TC-WW-26
    [Fact]
    public async Task Simulator_MakesAnActiveSimulatedWarning_WithAllowedValues()
    {
        var result = await SimulatedWarningGenerator.CreateRandomWarningAsync(_service);

        Assert.Equal(WarningOutcome.Ok, result.Outcome);
        var warning = result.Warning!;
        Assert.Equal(WarningSource.Simulated, warning.Source);
        Assert.StartsWith("[SIMULATED]", warning.Description);
        Assert.Contains(warning.Severity, WeatherWarning.AllowedSeverities);
        Assert.Equal(WarningStatus.Active, warning.GetStatus(DateTime.UtcNow));
    }
}
