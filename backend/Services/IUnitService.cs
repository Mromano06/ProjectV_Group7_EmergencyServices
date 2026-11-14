using MyApplication.Api.Dtos;
using MyApplication.Api.Models;
using MyApplication.Api.Enums;

namespace MyApplication.Api.Services;

// Matthew Romano - Nov 14th, 2026 - Unit Services Interface

public interface IUnitService
{
    Task<PagedResult<UnitDto>> ListAsync(UnitStatus? status, UnitType? type, int page, int pageSize, CancellationToken ct);
    Task<UnitDto?> GetAsync(Guid id, CancellationToken ct);
    Task<ServiceResult<UnitDto>> CreateAsync(UnitCreateRequest request, Guid userId, CancellationToken ct);
    Task<ServiceResult<UnitDto>> UpdateAsync(Guid id, UnitUpdateRequest request, Guid userId, CancellationToken ct);
    Task<ServiceResult<UnitDto>> DeactivateAsync(Guid id, Guid userId, CancellationToken ct);
}
