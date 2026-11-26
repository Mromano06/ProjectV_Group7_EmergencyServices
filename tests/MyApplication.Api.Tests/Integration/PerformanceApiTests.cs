using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using MyApplication.Api.Dtos;
using Xunit.Abstractions;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// ES-NFR-008: creating an incident and assigning a unit shall each complete in under 2 seconds,
/// for 95% of requests. Each is timed over 40 requests through the whole API (routing, login and
/// role checks, validation, database), after one warm-up request (the very first request also
/// starts the app, which isn't what a dispatcher waits for).
///
/// This runs on an in-memory database. For the real numbers on the demo machine (Docker + SQL Server)
/// run scripts/perf-test.ps1. Run only these tests with:
///     dotnet test --filter "Category=Performance"
/// </summary>
[Trait("Category", "Performance")]
[Trait("Area", "Dispatch")]
public class PerformanceApiTests : IClassFixture<EmergencyApiFactory>
{
    private const int Runs = 40;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

    private readonly HttpClient _admin;
    private readonly HttpClient _dispatcher;
    private readonly ITestOutputHelper _output;

    public PerformanceApiTests(EmergencyApiFactory factory, ITestOutputHelper output)
    {
        _admin = factory.CreateClientAs(Roles.Admin);
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
        _output = output;
    }

    /// <summary>The time 95% of requests came in under (nearest-rank 95th percentile).</summary>
    public static TimeSpan Percentile95(IReadOnlyCollection<TimeSpan> times)
    {
        var sorted = times.OrderBy(t => t).ToList();
        var rank = (int)Math.Ceiling(0.95 * sorted.Count);
        return sorted[Math.Max(rank, 1) - 1];
    }

    private void Report(string what, IReadOnlyCollection<TimeSpan> times)
    {
        _output.WriteLine($"{what}: {times.Count} requests, 95th percentile {Percentile95(times).TotalMilliseconds:F0} ms, " +
                          $"average {times.Average(t => t.TotalMilliseconds):F0} ms, slowest {times.Max().TotalMilliseconds:F0} ms " +
                          $"(limit {Limit.TotalMilliseconds:F0} ms)");
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task<HttpResponseMessage>> call, HttpStatusCode expected)
    {
        var watch = Stopwatch.StartNew();
        var response = await call();
        watch.Stop();
        await response.ExpectAsync(expected); // a fast error doesn't count as a pass
        return watch.Elapsed;
    }

    // TC-API-PERF-01
    [Fact]
    [Trait("Requirement", "ES-NFR-008")]
    public async Task CreatingAnIncident_95PercentUnder2Seconds()
    {
        await _dispatcher.CreateIncidentAsync(); // warm-up

        var times = new List<TimeSpan>();
        for (var i = 0; i < Runs; i++)
            times.Add(await TimeAsync(
                () => _dispatcher.PostAsJsonAsync("/api/emergency/v1/incidents", TestData.NewIncident()),
                HttpStatusCode.Created));

        Report("Create incident", times);
        Assert.True(Percentile95(times) < Limit, $"95th percentile was {Percentile95(times).TotalMilliseconds:F0} ms");
    }

    // TC-API-PERF-02
    [Fact]
    [Trait("Requirement", "ES-NFR-008")]
    public async Task AssigningAUnit_95PercentUnder2Seconds()
    {
        // Set-up (not timed): one available unit and one incident per run, plus a warm-up.
        var station = await _admin.CreateStationAsync();
        var units = new List<UnitDto>();
        var incidents = new List<IncidentDetailDto>();
        for (var i = 0; i <= Runs; i++)
        {
            units.Add(await _admin.CreateUnitAsync(station.Id));
            incidents.Add(await _dispatcher.CreateIncidentAsync());
        }
        await (await _dispatcher.AssignAsync(incidents[Runs].Id, units[Runs].Id)).ExpectAsync(HttpStatusCode.Created); // warm-up

        var times = new List<TimeSpan>();
        for (var i = 0; i < Runs; i++)
        {
            var (incident, unit) = (incidents[i], units[i]);
            times.Add(await TimeAsync(() => _dispatcher.AssignAsync(incident.Id, unit.Id), HttpStatusCode.Created));
        }

        Report("Assign unit", times);
        Assert.True(Percentile95(times) < Limit, $"95th percentile was {Percentile95(times).TotalMilliseconds:F0} ms");
    }

    // TC-PERF-03
    [Theory]
    [InlineData(new[] { 100 }, 100)]
    [InlineData(new[] { 5, 1, 3, 2, 4 }, 5)]                     // ceil(0.95*5)=5 -> the slowest of 5
    [InlineData(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }, 19)] // 19th of 20
    [Trait("Requirement", "ES-NFR-008")]
    public void Percentile95_IsTheNearestRank(int[] millis, int expected)
    {
        Assert.Equal(expected, Percentile95(millis.Select(m => TimeSpan.FromMilliseconds(m)).ToList()).TotalMilliseconds);
    }
}
