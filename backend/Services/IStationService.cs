using MyApplication.Api.Dtos;
namespace MyApplication.Api.Services;

// Matthew Romano - Nov 14th, 2026 - Station Services Interface

public interface IStationService
{
    Task<IReadOnlyList<StationDto>> GetAllAsync(CancellationToken ct);
    Task<StationDto?> GetAsync(Guid id, CancellationToken ct);
    Task<ServiceResult<StationDto>> CreateAsync(StationSaveRequest req, Guid userId, CancellationToken ct);
    Task<ServiceResult<StationDto>> UpdateAsync(Guid id, StationSaveRequest req, Guid userId, CancellationToken ct);
}
