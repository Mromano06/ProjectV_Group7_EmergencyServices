using MyApplication.Api.Models;

namespace MyApplication.Api.Services;

// Matthew Romano - Nov 7th, 2026 - Hospital Opertions

/// <summary>
/// Provides read-only access to existing hospital information.
/// </summary>
public interface IHospitalService
{
    /// <summary>Retrieve all registered hospitals.</summary>
    Task<List<Hospital>> GetAllAsync();

    /// <summary>Retrieve a hospital by its ID.</summary>
    Task<Hospital?> GetByIdAsync(Guid id);
}

