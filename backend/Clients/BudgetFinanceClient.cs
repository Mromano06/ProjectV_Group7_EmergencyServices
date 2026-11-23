using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MyApplication.Api.Dtos;
using MyApplication.Api.Integration;

namespace MyApplication.Api.Clients;

// Matthew Romano - Nov 14th, 2026 - Buudget/Finances Client Connection

/// <summary>ES-FR-025 / SH-C08/09. Budget owns its own data; calls go through the gateway.</summary>
public sealed class BudgetFinanceClient(HttpClient http, IServiceTokenProvider tokens)
{
    
    /// <summary>
    /// creates and sends a request to budget, and returns the results
    /// </summary>
    /// <param name="ct">token</param>
    /// <returns>response contents (json)</returns>
    public async Task<JsonElement> GetAllocationAsync(CancellationToken ct)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "api/budget/v1/allocations/emergency", ct);
        return await SendJsonAsync(request, ct);
    }

    /// <summary>
    /// sends a post request to budget , and returns the results
    /// </summary>
    /// <param name="body">request body</param>
    /// <param name="ct">token</param>
    /// <returns>reponse contents</returns>
    public async Task<JsonElement> SubmitExpenseAsync(ExpenseRequest body, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "api/budget/v1/expenses", ct);
        request.Content = JsonContent.Create(body);
        return await SendJsonAsync(request, ct);
    }

    /// <summary>
    /// asynchronous helper function to generate valid http request
    /// </summary>
    /// <param name="method">http method</param>
    /// <param name="path">http path</param>
    /// <param name="ct">token</param>
    /// <returns>request</returns>
    /// <exception cref="ExternalServiceException"></exception>
    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path, CancellationToken ct)
    {
        if (http.BaseAddress is null)
            throw new ExternalServiceException("API gateway URL is not configured.");
        var token = await tokens.GetTokenAsync(ct);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>
    /// asynchronous helper function that sends request and gives you back the json results
    /// </summary>
    /// <param name="request">http request</param>
    /// <param name="ct">token</param>
    /// <returns>json body</returns>
    /// <exception cref="ExternalServiceException"></exception>
    private async Task<JsonElement> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new ExternalServiceException($"Budget service returned HTTP {(int)response.StatusCode}.", 502);
            if (response.Content.Headers.ContentLength == 0) return JsonDocument.Parse("{}").RootElement.Clone();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return json.RootElement.Clone();
        }
        catch (HttpRequestException ex) { throw new ExternalServiceException($"Budget service connection failed: {ex.GetType().Name}."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { throw new ExternalServiceException("Budget service request timed out.", 504); }
    }
}
