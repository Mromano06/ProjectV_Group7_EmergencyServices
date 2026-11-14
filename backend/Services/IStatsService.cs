using MyApplication.Api.Dtos;
namespace MyApplication.Api.Services;

// Matthew Romano - Nov 14th, 2026 - Stats Service Interface

public interface IStatsService
{
    Task<ServiceResult<StatsDto>> GetStatsAsync(DateTime from, DateTime to, CancellationToken ct);
    Task<ServiceResult<string>> ExportCsvAsync(DateTime from, DateTime to, CancellationToken ct);
}
