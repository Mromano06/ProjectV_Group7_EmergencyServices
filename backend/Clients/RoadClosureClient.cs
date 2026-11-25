using System.Net.Http.Headers;
using System.Text.Json;
using MyApplication.Api.Dtos;
using MyApplication.Api.Integration;
using MyApplication.Api.Services;

namespace MyApplication.Api.Clients;

/// <summary>
/// ES-FR-010 / SH-C17: reads current road closures from Public Transport, who collect them from
/// every team (SH-C16). Never touches their database.
/// </summary>
public sealed class RoadClosureClient(HttpClient http, IServiceTokenProvider tokens, IConfiguration config)
{
    /// <summary>Confirmed by Transit (Nov 25). Can be changed with "Services:RoadClosuresPath".</summary>
    public const string DefaultPath = "api/transport/v1/road-closures";

    /// <summary>ES-FR-010: closures within this distance of an incident are shown.</summary>
    public const double NearbyKm = 1.0;

    /// <summary>
    /// Road closures within 1 km of the incident, nearest first. Closures that come without
    /// coordinates can't be measured, so they are included at the end with DistanceKm = null
    /// rather than silently hidden from the dispatcher.
    /// </summary>
    public async Task<IReadOnlyList<NearbyRoadClosureDto>> GetNearbyAsync(
        double latitude, double longitude, CancellationToken ct)
    {
        if (http.BaseAddress is null)
            throw new ExternalServiceException("Road-closure service URL is not configured.");
        var token = await tokens.GetTokenAsync(ct);
        var path = config["Services:RoadClosuresPath"];
        using var request = new HttpRequestMessage(HttpMethod.Get, string.IsNullOrWhiteSpace(path) ? DefaultPath : path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new ExternalServiceException($"Road-closure service returned HTTP {(int)response.StatusCode}.", 502);
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            // Transit hasn't published its JSON yet, so accept the likely shapes:
            // [ ... ] or { items: [...] } / { roadClosures: [...] }, coordinates at the top or under "location".
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("items", out var items)) root = items;
                else if (root.TryGetProperty("roadClosures", out var closures)) root = closures;
            }
            if (root.ValueKind != JsonValueKind.Array)
                throw new ExternalServiceException("Unexpected road-closure response schema.", 502);

            var nearby = new List<NearbyRoadClosureDto>();
            foreach (var row in root.EnumerateArray())
            {
                var place = row.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object ? loc : row;
                double? lat = Number(place, "latitude", "lat");
                double? lon = Number(place, "longitude", "lng", "lon");
                double? distance = lat.HasValue && lon.HasValue
                    ? DispatchService.DistanceKm(latitude, longitude, lat.Value, lon.Value)
                    : null;
                if (distance > NearbyKm) continue;

                nearby.Add(new NearbyRoadClosureDto(
                    Text(row, "id", "closureId") ?? string.Empty,
                    Text(row, "description", "reason", "name") ?? Text(place, "address", "street"),
                    Date(row, "expectedEndAt", "estimatedEndAt", "endsAt", "endTime", "expectedEndTime"),
                    lat, lon, distance));
            }
            return nearby.OrderBy(x => x.DistanceKm is null).ThenBy(x => x.DistanceKm).ToList();
        }
        catch (HttpRequestException) { throw new ExternalServiceException("Road-closure service is unreachable.", 503); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { throw new ExternalServiceException("Road-closure service timed out.", 504); }
        catch (JsonException) { throw new ExternalServiceException("Unexpected road-closure JSON payload.", 502); }
    }

    private static double? Number(JsonElement e, params string[] names)
    {
        foreach (var n in names)
            if (e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        return null;
    }

    private static string? Text(JsonElement e, params string[] names)
    {
        foreach (var n in names)
            if (e.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                return v.ToString();
        return null;
    }

    private static DateTimeOffset? Date(JsonElement e, params string[] names)
    {
        foreach (var n in names)
            if (e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(v.GetString(), out var parsed)) return parsed;
        return null;
    }
}
