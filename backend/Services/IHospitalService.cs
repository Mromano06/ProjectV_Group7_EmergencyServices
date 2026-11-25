using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Services;

// Matthew Romano - Nov 7th, 2026 - Hospital Opertions Interface

/// <summary>
/// Provides read-only access to existing hospital information.
/// </summary>
public interface IHospitalService
{
    /// <summary>Retrieve all registered hospitals.</summary>
    Task<List<Hospital>> GetAllAsync();

    /// <summary>Retrieve a hospital by its ID.</summary>
    Task<Hospital?> GetByIdAsync(Guid id);
/// <summary>Get hospitals, optionally filtering by ER status and sorting by distance.</summary>
    Task<List<HospitalSearchResult>> SearchAsync(
        double? latitude, double? longitude, bool openOnly, CancellationToken ct);

    /// <summary>Register a new hospital (Admin only at controller level).</summary>
    Task<Hospital> CreateAsync(Hospital hospital, CancellationToken ct = default);

    /// <summary>Update hospital name, location, status and beds.</summary>
    Task<Hospital?> UpdateAsync(Guid id, Hospital hospital, CancellationToken ct = default);

    /// <summary>Update only ER status and available beds.</summary>
    Task<Hospital?> UpdateErAsync(Guid id, HospitalStatus status, int availableBeds, CancellationToken ct);
}

public sealed record HospitalSearchResult(Hospital Hospital, double? DistanceKm);
