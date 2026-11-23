using System.Net.Http.Headers;
using System.Text.Json;
using MyApplication.Api.Dtos;
using MyApplication.Api.Integration;
using MyApplication.Api.Services;

namespace MyApplication.Api.Clients;

/// <summary>ES-FR-010 / SH-C03: calls Maintenance via the gateway, never its database.</summary>
public sealed class RoadClosureClient(HttpClient http, IServiceTokenProvider tokens)
{
    // Gets nearby incidents to determine who is effected
    // TODO: Also decide if this works on Lat/long
    /// <summary>
    /// grabs road closures from an incident location by asking maintenance
    /// returns road closures within 1km of incident
    /// </summary>
    /// <param name="latitude">lat of incident</param>
    /// <param name="longitude">long of incident</param>
    /// <param name="ct">token</param>
    /// <returns>road closures (list)</returns>
    /// <exception cref="ExternalServiceException"></exception>
    public async Task<IReadOnlyList<NearbyRoadClosureDto>> GetNearbyAsync(
        double latitude, double longitude, CancellationToken ct)
    {
        if (http.BaseAddress is null)
            throw new ExternalServiceException("API gateway URL is not configured.");
        var token = await tokens.GetTokenAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/maintenance/v1/road-closures?active=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new ExternalServiceException($"Maintenance returned HTTP {(int)response.StatusCode}.", 502);
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            // Provisional adapter: assume array or {items:[...]}, each item having
            // id, latitude, longitude, expectedEndAt, description.
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items)) root = items;
            if (root.ValueKind != JsonValueKind.Array)
                throw new ExternalServiceException("Unexpected Maintenance response schema.", 502);
            var nearby = new List<NearbyRoadClosureDto>();
            foreach (var row in root.EnumerateArray())
            {
                if (!row.TryGetProperty("latitude", out var lat) || !lat.TryGetDouble(out var a) ||
                    !row.TryGetProperty("longitude", out var lon) || !lon.TryGetDouble(out var b))
                    throw new ExternalServiceException("Maintenance response has no closure coordinates.", 502);
                var distance = DispatchService.DistanceKm(latitude, longitude, a, b);
                if (distance > 1.0) continue;
                string id = row.TryGetProperty("id", out var idElement) ? idElement.ToString() : string.Empty;
                string? description = row.TryGetProperty("description", out var desc) ? desc.GetString() : null;
                DateTimeOffset? end = null;
                if (row.TryGetProperty("expectedEndAt", out var endElement) && endElement.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(endElement.GetString(), out var parsed)) end = parsed;
                nearby.Add(new NearbyRoadClosureDto(id, description, end, a, b, distance));
            }
            return nearby.OrderBy(x => x.DistanceKm).ToList();
        }
        catch (HttpRequestException) { throw new ExternalServiceException("Maintenance service is unreachable.", 503); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { throw new ExternalServiceException("Maintenance service timed out.", 504); }
        catch (JsonException) { throw new ExternalServiceException("Unexpected Maintenance JSON payload.", 502); }
    }
}
