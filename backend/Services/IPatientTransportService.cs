using MyApplication.Api.Dtos;

namespace MyApplication.Api.Services;

// Matt Romano - 25th, Nov - Patient service interface

/// <summary>ES-FR-014: manage patient transport records without medical histories.</summary>
public interface IPatientTransportService
{
    Task<IReadOnlyList<PatientTransportRecordDto>?> ListAsync(Guid incidentId, CancellationToken ct);
    Task<PatientTransportRecordDto?> GetByIdAsync(Guid incidentId, Guid transportId, CancellationToken ct);
    Task<PatientTransportRecordDto> CreateAsync(Guid incidentId, CreatePatientTransportRequest request, CancellationToken ct);
}
