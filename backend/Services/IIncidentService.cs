using MyApplication.Api.Dtos;
using MyApplication.Api.Models;
using MyApplication.Api.Enums;
namespace MyApplication.Api.Services;

// Matthew Romano - Nov 14th, 2026 - Incident Service Interface

public interface IIncidentService
{
    Task<PagedResult<IncidentSummaryDto>> ListAsync(IncidentStatus? status, IncidentType? type, int page, int pageSize, CancellationToken ct);
    Task<IncidentDetailDto?> GetAsync(Guid id, CancellationToken ct);
    Task<ServiceResult<IncidentDetailDto>> CreateAsync(IncidentCreateRequest request, Guid userId, CancellationToken ct);
    Task<ServiceResult<IncidentDetailDto>> ChangeStatusAsync(Guid id, ChangeIncidentStatusRequest request, Guid userId, CancellationToken ct);
    Task<ServiceResult<IncidentDetailDto>> FileReportAsync(Guid id, FileAfterActionReportRequest request, Guid userId, CancellationToken ct);
}