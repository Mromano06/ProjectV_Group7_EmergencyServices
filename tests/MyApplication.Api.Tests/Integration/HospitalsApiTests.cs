using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// Hospitals (ES-FR-012, ES-FR-013): ER status and beds, Admin updates, nearest open ER.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Hospitals")]
public class HospitalsApiTests : IClassFixture<EmergencyApiFactory>
{
    private const string Url = "/api/emergency/v1/hospitals";

    private readonly EmergencyApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _dispatcher;
    private readonly HttpClient _responder;
    private readonly HttpClient _viewer;

    public HospitalsApiTests(EmergencyApiFactory factory)
    {
        _factory = factory;
        _admin = factory.CreateClientAs(Roles.Admin);
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
        _responder = factory.CreateClientAs(Roles.Responder);
        _viewer = factory.CreateClientAs(Roles.Viewer);
    }

    private static object NewHospital(double lat = 43.40, double lon = -80.32, string erStatus = "Open",
        int beds = 10, string? name = null) => new
    {
        name = name ?? "Test Hospital " + Guid.NewGuid().ToString("N")[..6],
        location = new { latitude = lat, longitude = lon, address = "700 Coronation Blvd, Cambridge" },
        erStatus,
        availableBeds = beds
    };

    private async Task<HospitalDto> CreateAsync(double lat = 43.40, double lon = -80.32,
        string erStatus = "Open", int beds = 10)
    {
        var response = await _admin.PostAsJsonAsync(Url, NewHospital(lat, lon, erStatus, beds));
        return await response.ExpectAsync<HospitalDto>(HttpStatusCode.Created);
    }

    // TC-API-HOS-01
    [Fact]
    [Trait("Requirement", "ES-FR-012")]
    public async Task Admin_CreatesHospital_WithLocationErStatusAndBeds()
    {
        var response = await _admin.PostAsJsonAsync(Url, NewHospital(erStatus: "Diverting", beds: 7));

        var created = await response.ExpectAsync<HospitalDto>(HttpStatusCode.Created);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("Diverting", created.ErStatus);
        Assert.Equal(7, created.AvailableBeds);
        Assert.Equal(43.40, created.Location.Latitude);
        Assert.Equal("700 Coronation Blvd, Cambridge", created.Location.Address);
        Assert.Equal(DateTimeKind.Utc, created.UpdatedAt.Kind); // SH-I04
    }

    // TC-API-HOS-02
    [Fact]
    [Trait("Requirement", "ES-FR-012")]
    public async Task OneHospital_CanBeReadById_AndUnknownReturns404()
    {
        var created = await CreateAsync();

        var found = await (await _responder.GetAsync($"{Url}/{created.Id}")).ExpectAsync<HospitalDto>(HttpStatusCode.OK);
        Assert.Equal(created.Name, found.Name);

        var missing = await _responder.GetAsync($"{Url}/{Guid.NewGuid()}");
        Assert.Equal("hospital_not_found", await missing.ExpectErrorAsync(HttpStatusCode.NotFound));
    }

    // TC-API-HOS-03
    [Fact]
    [Trait("Requirement", "ES-FR-012")]
    public async Task Admin_UpdatesErStatusAndBeds_AndTheChangeTimeIsReturned()
    {
        var created = await CreateAsync(beds: 12);
        await Task.Delay(20); // so the change time is clearly later than the creation time

        var response = await _admin.PatchAsJsonAsync($"{Url}/{created.Id}/er", new { erStatus = "Diverting", availableBeds = 0 });

        var updated = await response.ExpectAsync<HospitalDto>(HttpStatusCode.OK);
        Assert.Equal("Diverting", updated.ErStatus);
        Assert.Equal(0, updated.AvailableBeds);
        Assert.True(updated.UpdatedAt > created.UpdatedAt, "UpdatedAt should move forward when the ER changes");

        var reread = await (await _dispatcher.GetAsync($"{Url}/{created.Id}")).ExpectAsync<HospitalDto>(HttpStatusCode.OK);
        Assert.Equal("Diverting", reread.ErStatus);
        Assert.Equal(updated.UpdatedAt, reread.UpdatedAt);
    }

    // TC-API-HOS-04
    [Fact]
    [Trait("Requirement", "ES-FR-012")]
    public async Task Admin_UpdatesTheWholeHospital_WithPut()
    {
        var created = await CreateAsync();

        var response = await _admin.PutAsJsonAsync($"{Url}/{created.Id}",
            NewHospital(lat: 43.41, lon: -80.33, erStatus: "Closed", beds: 0, name: "Renamed Hospital"));

        var updated = await response.ExpectAsync<HospitalDto>(HttpStatusCode.OK);
        Assert.Equal("Renamed Hospital", updated.Name);
        Assert.Equal("Closed", updated.ErStatus);
        Assert.Equal(43.41, updated.Location.Latitude);
    }

    // TC-API-HOS-05
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task OnlyAdmin_CanChangeHospitals()
    {
        var created = await CreateAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await _dispatcher.PostAsJsonAsync(Url, NewHospital())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _responder.PatchAsJsonAsync($"{Url}/{created.Id}/er", new { erStatus = "Closed", availableBeds = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _viewer.GetAsync(Url)).StatusCode); // Endpoints tab: Dispatcher, Responder, Admin
    }

    // TC-API-HOS-06
    [Theory]
    [Trait("Requirement", "ES-NFR-004")]
    [InlineData(95.0, -80.3, "Open", 5)]    // impossible latitude
    [InlineData(43.4, -80.3, "Open", -1)]   // negative beds
    public async Task InvalidHospital_Returns400(double lat, double lon, string erStatus, int beds)
    {
        var response = await _admin.PostAsJsonAsync(Url, NewHospital(lat, lon, erStatus, beds));

        Assert.Equal("invalid_hospital", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-HOS-07
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task ErUpdate_WithNegativeBeds_Returns400_AndUnknownHospital_Returns404()
    {
        var created = await CreateAsync();

        var bad = await _admin.PatchAsJsonAsync($"{Url}/{created.Id}/er", new { erStatus = "Open", availableBeds = -3 });
        Assert.Equal("invalid_er_update", await bad.ExpectErrorAsync(HttpStatusCode.BadRequest));

        var unknown = await _admin.PatchAsJsonAsync($"{Url}/{Guid.NewGuid()}/er", new { erStatus = "Open", availableBeds = 3 });
        Assert.Equal("hospital_not_found", await unknown.ExpectErrorAsync(HttpStatusCode.NotFound));
    }

    // TC-API-HOS-08
    [Fact]
    [Trait("Requirement", "ES-FR-013")]
    public async Task NearestOpenHospitals_AreSortedByDistance_AndSkipDivertingAndClosed()
    {
        // A spot far away from every other test's hospitals, so ours are the nearest.
        const double lat = 10.0, lon = 10.0;
        var farOpen = await CreateAsync(lat + 0.05, lon);
        var nearOpen = await CreateAsync(lat + 0.01, lon);
        var nearestDiverting = await CreateAsync(lat + 0.001, lon, "Diverting");
        var nearestClosed = await CreateAsync(lat + 0.002, lon, "Closed");

        var list = await (await _dispatcher.GetAsync($"{Url}?near={lat},{lon}&open=true"))
            .ExpectAsync<List<HospitalDto>>(HttpStatusCode.OK);

        Assert.Equal(nearOpen.Id, list[0].Id);
        Assert.Equal(farOpen.Id, list[1].Id);
        Assert.True(list[0].DistanceKm < list[1].DistanceKm);
        Assert.All(list, h => Assert.Equal("Open", h.ErStatus));
        Assert.DoesNotContain(list, h => h.Id == nearestDiverting.Id || h.Id == nearestClosed.Id);
    }

    // TC-API-HOS-09
    [Fact]
    [Trait("Requirement", "ES-FR-013")]
    public async Task Near_WithBadCoordinates_Returns400()
    {
        var response = await _dispatcher.GetAsync($"{Url}?near=abc,200");

        Assert.Equal("invalid_near", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-HOS-10
    [Fact]
    [Trait("Requirement", "ES-FR-012")]
    public async Task Summary_AddsUpEveryHospital()
    {
        await CreateAsync(beds: 4);
        var all = await (await _dispatcher.GetAsync(Url)).ExpectAsync<List<HospitalDto>>(HttpStatusCode.OK);

        var summary = await (await _dispatcher.GetAsync($"{Url}/summary")).ExpectAsync<JsonElement>(HttpStatusCode.OK);

        Assert.Equal(all.Count, summary.GetProperty("hospitalCount").GetInt32());
        Assert.Equal(all.Sum(h => h.AvailableBeds), summary.GetProperty("availableBeds").GetInt32());
        Assert.Equal(all.Count(h => h.ErStatus == "Open"), summary.GetProperty("openHospitals").GetInt32());
    }

    // TC-API-HOS-11
    [Fact]
    [Trait("Requirement", "Data integrity")]
    public async Task Database_RejectsNegativeBeds()
    {
        // The CK_Hospitals_AvailableBeds check constraint must stop bad data even if code forgets to.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _factory.SeedAsync(db => db.Hospitals.Add(new Hospital
        {
            Name = "Bad Hospital",
            Location = new Location { Latitude = 43.4, Longitude = -80.3 },
            ErStatus = HospitalStatus.Open,
            AvailableBeds = -1
        })));
    }
}
