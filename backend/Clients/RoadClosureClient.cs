using System.Net.Http.Headers;
using System.Text.Json;
using MyApplication.Api.Dtos;
using MyApplication.Api.Integration;
using MyApplication.Api.Services;

namespace MyApplication.Api.Clients;

/// <summary>
/// ES-FR-010 / SH-C17: reads current road closures from Public Transport, who collect them from
/// every team (SH-C16). Never touches their database.
/// Transit's Swagger (Nov 25): GET /api/transport/v1/road-closures returns disruptions.DisruptionResponse[]
/// (snake_case): id, title, description, affected_area, affected_segments, start_time, end_time, status...
/// with NO coordinates. It needs no login.
/// </summary>
public sealed class RoadClosureClient(HttpClient http, IServiceTokenProvider tokens, IConfiguration config)
{
    /// <summary>From Transit's Swagger (Nov 25). Can be changed with "Services:RoadClosuresPath".</summary>
    public const string DefaultPath = "api/transport/v1/road-closures";

    /// <summary>ES-FR-010: closures within this distance of an incident are shown.</summary>
    public const double NearbyKm = 1.0;

    /// <summary>
    /// Active road closures near the incident, for the dispatcher (ES-FR-010).
    /// Transit sends a free-text area and street segments, not coordinates, so a closure counts as near when:
    ///   - it has coordinates within 1 km of the incident (MatchedBy "distance"), or
    ///   - it names the incident's street in affected_area, affected_segments, an address, or its title
    ///     (MatchedBy "street"). "King St W" also matches a closure on plain "King St".
    /// Closures that have ended (status resolved/closed/... or end_time in the past) are left out.
    /// Distance matches come first, nearest first.
    /// </summary>
    public async Task<IReadOnlyList<NearbyRoadClosureDto>> GetNearbyAsync(
        double latitude, double longitude, string? incidentAddress, CancellationToken ct)
    {
        if (http.BaseAddress is null)
            throw new ExternalServiceException("Road-closure service URL is not configured.");
        var path = config["Services:RoadClosuresPath"];
        using var request = new HttpRequestMessage(HttpMethod.Get, string.IsNullOrWhiteSpace(path) ? DefaultPath : path);

        // Our service-account token when we have one (SH-S07); Registration has none for services yet.
        string? token = null;
        try { token = await tokens.GetTokenAsync(ct); }
        catch (ExternalServiceException) { /* no service-account token configured yet */ }
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var incidentStreet = SharedAddress.NormalizeStreet(SharedAddress.Parse(incidentAddress).StreetName);
        if (incidentStreet == "unknown location") incidentStreet = string.Empty;
        var now = DateTimeOffset.UtcNow;
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new ExternalServiceException($"Road-closure service returned HTTP {(int)response.StatusCode}.", 502);
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            // Accept [ ... ], { items: [...] } or { roadClosures: [...] }.
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
                if (row.ValueKind != JsonValueKind.Object) continue;
                var endsAt = Date(row, "end_time", "expectedEndAt", "estimatedEndAt", "endsAt", "endTime", "expectedEndTime", "reopensAt");
                if (IsOver(Text(row, "status"), endsAt, now)) continue;
                var place = row.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object ? loc : row;
                double? lat = Number(place, "latitude", "lat");
                double? lon = Number(place, "longitude", "lng", "lon");
                var address = AddressOf(row);

                double? distance = lat.HasValue && lon.HasValue
                    ? DispatchService.DistanceKm(latitude, longitude, lat.Value, lon.Value)
                    : null;
                string? matchedBy =
                    distance <= NearbyKm ? "distance"
                    : distance is null && OnStreet(incidentStreet, StreetTexts(row, address)) ? "street"
                    : null;
                if (matchedBy is null) continue;

                nearby.Add(new NearbyRoadClosureDto(
                    Text(row, "id", "closureId") ?? string.Empty,
                    DescribeClosure(row) ?? Describe(address),
                    endsAt,
                    lat, lon, distance, matchedBy));
            }
            return nearby.OrderBy(x => x.DistanceKm is null).ThenBy(x => x.DistanceKm).ToList();
        }
        catch (HttpRequestException) { throw new ExternalServiceException("Road-closure service is unreachable.", 503); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { throw new ExternalServiceException("Road-closure service timed out.", 504); }
        catch (JsonException) { throw new ExternalServiceException("Unexpected road-closure JSON payload.", 502); }
    }

    private static readonly string[] EndedStatuses =
        ["resolved", "closed", "cancelled", "canceled", "ended", "inactive", "completed", "expired", "cleared"];

    /// <summary>True when the closure is finished: an ended status, or an end time already past.</summary>
    private static bool IsOver(string? status, DateTimeOffset? endsAt, DateTimeOffset now) =>
        (status is not null && EndedStatuses.Contains(status.Trim().ToLowerInvariant())) ||
        (endsAt is not null && endsAt <= now);

    /// <summary>Everything in a closure that can name a street.</summary>
    private static IEnumerable<string?> StreetTexts(JsonElement row, SharedAddress? address)
    {
        yield return address?.StreetName;
        yield return Text(row, "affected_area");
        if (row.TryGetProperty("affected_segments", out var segments) && segments.ValueKind == JsonValueKind.Array)
            foreach (var s in segments.EnumerateArray())
                if (s.ValueKind == JsonValueKind.String) yield return s.GetString();
        yield return Text(row, "title");
    }

    /// <summary>
    /// True when any text names the incident's street as whole words ("king st w" in "king st w between ...").
    /// A street with a direction ("king st w") also matches the plain street ("king st").
    /// </summary>
    public static bool OnStreet(string incidentStreet, IEnumerable<string?> texts)
    {
        if (incidentStreet.Length == 0) return false;
        var wanted = new List<string> { incidentStreet };
        var words = incidentStreet.Split(' ');
        if (words.Length > 2 && words[^1] is "n" or "s" or "e" or "w")
            wanted.Add(string.Join(' ', words[..^1]));

        foreach (var text in texts)
        {
            var normalized = $" {SharedAddress.NormalizeStreet(text)} ";
            if (wanted.Any(w => normalized.Contains($" {w} "))) return true;
        }
        return false;
    }

    /// <summary>"Title – affected area" (or whichever of title, description, reason, area it has).</summary>
    private static string? DescribeClosure(JsonElement row)
    {
        var title = Text(row, "title", "description", "reason", "name");
        var area = Text(row, "affected_area");
        if (title is null) return area;
        return area is null || title.Contains(area, StringComparison.OrdinalIgnoreCase) ? title : $"{title} – {area}";
    }

    /// <summary>
    /// The closure's address in the shared shape: an "address" object, an address string,
    /// or streetNumber/streetName/city/postalCode directly on the closure.
    /// </summary>
    private static SharedAddress? AddressOf(JsonElement row)
    {
        if (row.TryGetProperty("address", out var a))
        {
            if (a.ValueKind == JsonValueKind.String) return SharedAddress.Parse(a.GetString());
            if (a.ValueKind == JsonValueKind.Object) return FromParts(a);
        }
        return FromParts(row);
    }

    private static SharedAddress? FromParts(JsonElement e)
    {
        var street = Text(e, "streetName", "street");
        return street is null
            ? null
            : new SharedAddress(Text(e, "streetNumber", "number"), street, Text(e, "city"), Text(e, "postalCode"));
    }

    private static string? Describe(SharedAddress? a) =>
        a is null ? null : string.Join(", ", new[] { $"{a.StreetNumber} {a.StreetName}".Trim(), a.City }.Where(x => !string.IsNullOrWhiteSpace(x)));

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
