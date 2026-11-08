using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Tests;

/// <summary>
/// Tests for the rules built into the WeatherWarning class itself
/// (is it active, what's its status, ending it early).
/// </summary>
public class WeatherWarningModelTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static WeatherWarning MakeWarning(DateTime startsAt, DateTime endsAt) => new()
    {
        Type = "Snowfall",
        Severity = "Warning",
        Area = "City-wide",
        StartsAt = startsAt,
        EndsAt = endsAt
    };

    // TC-WW-01
    [Fact]
    public void IsActive_IsTrue_WhenNowIsInsideTheTimeWindow()
    {
        var warning = MakeWarning(Now.AddHours(-1), Now.AddHours(1));

        Assert.True(warning.IsActive(Now));
        Assert.Equal(WarningStatus.Active, warning.GetStatus(Now));
    }

    // TC-WW-02
    [Fact]
    public void Warning_ExpiresAutomatically_OnceEndsAtHasPassed()
    {
        var warning = MakeWarning(Now.AddHours(-3), Now.AddHours(-1));

        Assert.False(warning.IsActive(Now));
        Assert.Equal(WarningStatus.Ended, warning.GetStatus(Now));
    }

    // TC-WW-03
    [Fact]
    public void Warning_IsScheduled_BeforeItStarts()
    {
        var warning = MakeWarning(Now.AddHours(1), Now.AddHours(5));

        Assert.False(warning.IsActive(Now));
        Assert.Equal(WarningStatus.Scheduled, warning.GetStatus(Now));
    }

    // TC-WW-04
    [Fact]
    public void Warning_IsNotActive_AtExactlyItsEndTime()
    {
        var warning = MakeWarning(Now.AddHours(-1), Now);

        Assert.False(warning.IsActive(Now));
    }

    // TC-WW-05
    [Fact]
    public void Expire_MovesEndsAtToNow_ForAnActiveWarning()
    {
        var warning = MakeWarning(Now.AddHours(-1), Now.AddHours(4));

        warning.Expire(Now);

        Assert.Equal(Now, warning.EndsAt);
        Assert.Equal(WarningStatus.Ended, warning.GetStatus(Now));
    }

    // TC-WW-06
    [Fact]
    public void Expire_DoesNotChangeAWarningThatAlreadyEnded()
    {
        var originalEnd = Now.AddHours(-1);
        var warning = MakeWarning(Now.AddHours(-3), originalEnd);

        warning.Expire(Now);

        Assert.Equal(originalEnd, warning.EndsAt);
    }
}
