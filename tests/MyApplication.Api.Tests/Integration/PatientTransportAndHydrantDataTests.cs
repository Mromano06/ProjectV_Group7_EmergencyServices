using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyApplication.Api.Data;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// Database rules for patient transports (ES-FR-014) and hydrant usage (ES-FR-016).
/// There are no endpoints for these yet, so the tests put rows straight into the database.
/// When the endpoints are built, add API tests next to these.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Patients and hydrants")]
public class PatientTransportAndHydrantDataTests : IClassFixture<EmergencyApiFactory>
{
    private readonly EmergencyApiFactory _factory;
    private readonly HttpClient _dispatcher;

    public PatientTransportAndHydrantDataTests(EmergencyApiFactory factory)
    {
        _factory = factory;
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
    }

    private async Task<Hospital> SeedHospitalAsync()
    {
        var hospital = new Hospital
        {
            Name = "Test Hospital " + Guid.NewGuid().ToString("N")[..6],
            Location = "835 King St W, Kitchener",
            TotalRooms = 10,
            OccupiedRooms = 2
        };
        await _factory.SeedAsync(db => db.Hospitals.Add(hospital));
        return hospital;
    }

    // TC-DATA-01
    [Fact]
    [Trait("Requirement", "ES-FR-014")]
    public async Task PatientTransport_IsSaved_WithTriageLevelAndHospital()
    {
        var incident = await _dispatcher.CreateIncidentAsync(type: "Medical");
        var hospital = await SeedHospitalAsync();

        await _factory.SeedAsync(db => db.PatientTransports.Add(new PatientTransport
        {
            IncidentId = incident.Id, HospitalId = hospital.Id, TriageLevel = TriageLevel.Urgent
        }));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmergencyDbContext>();
        var saved = await db.PatientTransports.SingleAsync(p => p.IncidentId == incident.Id);
        Assert.Equal(TriageLevel.Urgent, saved.TriageLevel);
        Assert.Equal(hospital.Id, saved.HospitalId);
        Assert.NotEqual(default, saved.CreatedAt); // SH-D04
    }

    // TC-DATA-02
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task Database_RejectsPatientTransport_ToUnknownHospital()
    {
        var incident = await _dispatcher.CreateIncidentAsync(type: "Medical");

        await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            _factory.SeedAsync(db => db.PatientTransports.Add(new PatientTransport
            {
                IncidentId = incident.Id, HospitalId = Guid.NewGuid(), TriageLevel = TriageLevel.Emergency
            })));
    }

    // TC-DATA-03
    [Fact]
    [Trait("Requirement", "ES-FR-014")]
    public void PatientTransport_HasNoMedicalHistoryFields()
    {
        // "The API exposes no fields for diagnoses or medical history" – checked on the table itself.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmergencyDbContext>();
        var columns = db.Model.FindEntityType(typeof(PatientTransport))!
            .GetProperties().Select(p => p.Name).ToList();

        var allowed = new[]
        {
            "Id", "IncidentId", "TriageLevel", "HospitalId", "ResidentId",
            "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"
        };
        Assert.Empty(columns.Except(allowed));
    }

    // TC-DATA-04
    [Fact]
    [Trait("Requirement", "ES-FR-016")]
    public async Task OneFireIncident_CanRecordSeveralHydrants_NotSentYet()
    {
        var incident = await _dispatcher.CreateIncidentAsync(type: "Fire");

        await _factory.SeedAsync(db => db.HydrantUsages.AddRange(
            new HydrantUsage { IncidentId = incident.Id, HydrantId = Guid.NewGuid(), LitresUsed = 1200 },
            new HydrantUsage { IncidentId = incident.Id, HydrantId = Guid.NewGuid(), LitresUsed = 800 }));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmergencyDbContext>();
        var rows = await db.HydrantUsages.Where(h => h.IncidentId == incident.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Null(r.SentToMaintenanceAt)); // waiting to be sent (retry-able)
    }

    // TC-DATA-05
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task Database_RejectsNegativeHydrantLitres()
    {
        var incident = await _dispatcher.CreateIncidentAsync(type: "Fire");

        // The CK_HydrantUsages_LitresUsed check constraint must stop bad data even if code forgets to.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            _factory.SeedAsync(db => db.HydrantUsages.Add(new HydrantUsage
            {
                IncidentId = incident.Id, HydrantId = Guid.NewGuid(), LitresUsed = -5
            })));
    }
}
