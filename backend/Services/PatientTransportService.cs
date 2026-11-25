using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Data;
using MyApplication.Api.Dtos;
using MyApplication.Api.Models;

namespace MyApplication.Api.Services;

/// <summary>ES-FR-014: stores only incident, triage, destination and optional resident reference.</summary>
public sealed class PatientTransportService : IPatientTransportService
{
    private readonly EmergencyDbContext _db;

    public PatientTransportService(EmergencyDbContext db) => _db = db;

    public async Task<IReadOnlyList<PatientTransportRecordDto>?> ListAsync(
        Guid incidentId, CancellationToken ct)
    {
        if (!await _db.Incidents.AnyAsync(i => i.Id == incidentId, ct))
            return null;

        var transports = await _db.PatientTransports.AsNoTracking()
            .Where(t => t.IncidentId == incidentId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

        return transports.Select(ToDto).ToList();
    }

    public async Task<PatientTransportRecordDto?> GetByIdAsync(
        Guid incidentId, Guid transportId, CancellationToken ct)
    {
        var transport = await _db.PatientTransports.AsNoTracking()
            .FirstOrDefaultAsync(t => t.IncidentId == incidentId && t.Id == transportId, ct);
        return transport is null ? null : ToDto(transport);
    }

    public async Task<PatientTransportRecordDto> CreateAsync(
        Guid incidentId, CreatePatientTransportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await _db.Incidents.AnyAsync(i => i.Id == incidentId, ct))
            throw new KeyNotFoundException("Incident not found.");
        if (!await _db.Hospitals.AnyAsync(h => h.Id == request.HospitalId, ct))
            throw new ArgumentException("Destination hospital does not exist.");
        if (request.TriageLevel is null || !Enum.IsDefined(request.TriageLevel.Value))
            throw new ArgumentException("Provide a valid triage level.");
        if (request.ResidentId == Guid.Empty)
            throw new ArgumentException("ResidentId cannot be an empty GUID.");

        var transport = new PatientTransport
        {
            IncidentId = incidentId,
            HospitalId = request.HospitalId,
            TriageLevel = request.TriageLevel.Value,
            ResidentId = request.ResidentId
        };

        _db.PatientTransports.Add(transport);
        await _db.SaveChangesAsync(ct);
        return ToDto(transport);
    }

    private static PatientTransportRecordDto ToDto(PatientTransport transport) => new(
        transport.Id,
        transport.IncidentId,
        transport.TriageLevel,
        transport.HospitalId,
        transport.ResidentId,
        DateTime.SpecifyKind(transport.CreatedAt, DateTimeKind.Utc));
}

