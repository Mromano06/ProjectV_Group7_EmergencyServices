using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MyApplication.Api.Dtos;
using MyApplication.Api.Integration;

namespace MyApplication.Api.Clients;

/// <summary>ES-FR-026 / SH-C10/11. Never reads an HR database.</summary>
public sealed class StaffHrClient(HttpClient http, IServiceTokenProvider tokens)
{
    /// <summary>
    /// Gets staff counts based on position (asks staff gateway route)
    /// </summary>
    /// <param name="ct">token</param>
    /// <returns>json results body</returns>
    /// <exception cref="ExternalServiceException"></exception>
    public async Task<IReadOnlyList<ProfessionCount>> GetCountsAsync(CancellationToken ct)
    {
        using var request = await NewRequestAsync(HttpMethod.Get, "api/hr/v1/staff?department=emergency", ct);
        var root = await SendJsonAsync(request, ct);
        // Contract adapter assumption: HR returns [{"profession":"Paramedic", ...}, ...]
        // or {"staff":[...]}. Adapt only this method once HR confirms its payload.
        JsonElement rows = root;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("staff", out var staff)) rows = staff;
        if (rows.ValueKind != JsonValueKind.Array)
            throw new ExternalServiceException("Unexpected HR staff response schema.", 502);
        return rows.EnumerateArray()
            .Select(e => e.TryGetProperty("profession", out var p) ? p.GetString() : null)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .GroupBy(p => p!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProfessionCount(g.Key, g.Count()))
            .OrderBy(x => x.Profession).ToList();
    }

    /// <summary>
    /// Handles getting an exisiting request from the staffing client
    /// </summary>
    /// <param name="body">request body</param>
    /// <param name="ct">token</param>
    /// <returns>json results body</returns>
    public async Task<JsonElement> SubmitRequestAsync(StaffChangeRequest body, CancellationToken ct)
    {
        using var request = await NewRequestAsync(HttpMethod.Post, "api/hr/v1/staff-requests", ct);
        request.Content = JsonContent.Create(body);
        return await SendJsonAsync(request, ct);
    }

    /// <summary>
    ///  Handles creating a new request to the staff client
    /// </summary>
    /// <param name="method">http method</param>
    /// <param name="path">http path</param>
    /// <param name="ct">token</param>
    /// <returns>proper useable request</returns>
    /// <exception cref="ExternalServiceException"></exception>
    private async Task<HttpRequestMessage> NewRequestAsync(HttpMethod method, string path, CancellationToken ct)
    {
        if (http.BaseAddress is null)
            throw new ExternalServiceException("API gateway URL is not configured.");
        var token = await tokens.GetTokenAsync(ct);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>
    ///  Sends the actual JSON request to given route. returns response
    /// </summary>
    /// <param name="request">http request</param>
    /// <param name="ct">token</param>
    /// <returns>json response</returns>
    /// <exception cref="ExternalServiceException"></exception>
    private async Task<JsonElement> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new ExternalServiceException($"HR service returned HTTP {(int)response.StatusCode}.", 502);
            if (response.Content.Headers.ContentLength == 0) return JsonDocument.Parse("{}").RootElement.Clone();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return json.RootElement.Clone();
        }
        catch (HttpRequestException ex) { throw new ExternalServiceException($"HR service connection failed: {ex.GetType().Name}."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { throw new ExternalServiceException("HR service request timed out.", 504); }
    }
}
