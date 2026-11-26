using MyApplication.Api.Dtos;
using MyApplication.Api.Enums;
namespace MyApplication.Api.Services;

// Matthew Romano - Nov 14th, 2026 - Dispatch Services Interface

// Interface definitions for the Dispatch Service
public interface IDispatchService
{
    Task<ServiceResult<IReadOnlyList<AssignmentDto>>> AssignAsync(Guid incidentId, AssignUnitsRequest request,
        Guid userId, CancellationToken ct);
    Task<ServiceResult<AssignmentDto>> ReleaseAsync(Guid incidentId, Guid assignmentId, Guid userId, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<AssignmentDto>>> ListAsync(Guid incidentId, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<SuggestedUnitDto>>> SuggestAsync(Guid incidentId, UnitType type, CancellationToken ct);
}
