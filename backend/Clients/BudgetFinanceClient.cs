using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MyApplication.Api.Dtos;
using MyApplication.Api.Integration;

namespace MyApplication.Api.Clients;

// Matthew Romano - Nov 14th, 2026 - Buudget/Finances Client Connection

/// <summary>
/// ES-FR-025 / SH-C07 / SH-C08 / BF-07. Budget owns its own data; we only call its API.
/// Contract checked against Budget's live Swagger (Nov 25): allocations are looked up by
/// department code (/allocations/EMS), and Budget does not require a login token yet.
/// </summary>
public sealed class BudgetFinanceClient(HttpClient http, IServiceTokenProvider tokens)
{
    /// <summary>Our department code in Budget's system.</summary>
    public const string DepartmentCode = "EMS";

    /// <summary>GET /api/budget/v1/allocations/EMS: departmentCode, departmentName, allocated, spent, remaining, currency.</summary>
    public async Task<JsonElement> GetAllocationAsync(CancellationToken ct)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"api/budget/v1/allocations/{DepartmentCode}", ct);
        return await SendJsonAsync(request, ct);
    }

    /// <summary>POST /api/budget/v1/expenses. Budget answers 201 (approved) or 400 with the reason (over budget).</summary>
    public async Task<JsonElement> SubmitExpenseAsync(ExpenseRequest body, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "api/budget/v1/expenses", ct);
        request.Content = JsonContent.Create(body);
        return await SendJsonAsync(request, ct);
    }

    /// <summary>POST /api/budget/v1/funding-requests: ask for more money (BF-07). Urgent EMS requests go first.</summary>
    public async Task<JsonElement> SubmitFundingRequestAsync(BudgetFundingRequest body, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "api/budget/v1/funding-requests", ct);
        request.Content = JsonContent.Create(body);
        return await SendJsonAsync(request, ct);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path, CancellationToken ct)
    {
        if (http.BaseAddress is null)
            throw new ExternalServiceException("Budget service URL is not configured.");
        var request = new HttpRequestMessage(method, path);
        // Send our service-account token when we have one (SH-S07). Budget's API doesn't require a
        // token yet, so calls still go out without one instead of failing before they are sent.
        string? token = null;
        try { token = await tokens.GetTokenAsync(ct); }
        catch (ExternalServiceException) { /* no service-account token configured yet */ }
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>
    /// Sends the request and returns Budget's JSON.
    /// Budget saying "no" (400 / 409, e.g. over budget) comes back as that status with Budget's own
    /// message, so the user sees why. Budget being broken or unreachable becomes 502 / 503 / 504.
    /// </summary>
    private async Task<JsonElement> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                if (status is 400 or 409 or 422)
                    throw new ExternalServiceException(MessageFrom(text) ?? $"Budget refused the request (HTTP {status}).",
                        status == 409 ? 409 : 400);
                throw new ExternalServiceException($"Budget service returned HTTP {status}.", 502);
            }
            if (string.IsNullOrWhiteSpace(text)) return JsonDocument.Parse("{}").RootElement.Clone();
            using var json = JsonDocument.Parse(text);
            return json.RootElement.Clone();
        }
        catch (HttpRequestException ex) { throw new ExternalServiceException($"Budget service connection failed: {ex.GetType().Name}."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { throw new ExternalServiceException("Budget service request timed out.", 504); }
        catch (JsonException) { throw new ExternalServiceException("Budget service sent invalid JSON.", 502); }
    }

    /// <summary>Budget uses the shared error shape { error, message, traceId } (SH-I03).</summary>
    private static string? MessageFrom(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
