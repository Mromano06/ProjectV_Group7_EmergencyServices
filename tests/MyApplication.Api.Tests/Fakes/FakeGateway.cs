using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MyApplication.Api.Tests;

/// <summary>
/// A pretend API gateway standing in for OTHER teams' services (Budget, HR, Maintenance...).
/// Our API's HTTP clients are pointed at this instead of the network.
///
/// Tests tell it what to reply:
///     factory.Gateway.Respond(HttpMethod.Get, "/api/budget/v1/allocations/EMS", HttpStatusCode.OK, new { total = 1000 });
/// and can check what our API sent:
///     factory.Gateway.Requests
///
/// Anything a test hasn't set up gets 503 Service Unavailable (like the real service being down).
/// </summary>
public class FakeGateway : HttpMessageHandler
{
    public const string BaseUrl = "http://gateway.test/";

    public record RecordedRequest(string Method, string PathAndQuery, string? Authorization, string? Body);

    private readonly ConcurrentDictionary<string, (HttpStatusCode Status, string? Json)> _routes = new();
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    /// <summary>Everything our API sent to other services, oldest first.</summary>
    public IReadOnlyList<RecordedRequest> Requests => _requests.ToList();

    /// <summary>Set the reply for one method + path (path can include the ?query).</summary>
    public void Respond(HttpMethod method, string pathAndQuery, HttpStatusCode status, object? body = null)
    {
        var json = body is null ? null : JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        _routes[Key(method.Method, pathAndQuery)] = (status, json);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var pathAndQuery = request.RequestUri!.PathAndQuery;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue(new RecordedRequest(
            request.Method.Method, pathAndQuery, request.Headers.Authorization?.ToString(), body));

        // Exact match (with query) first, then path only.
        if (!_routes.TryGetValue(Key(request.Method.Method, pathAndQuery), out var reply) &&
            !_routes.TryGetValue(Key(request.Method.Method, request.RequestUri.AbsolutePath), out reply))
        {
            reply = (HttpStatusCode.ServiceUnavailable, "{\"error\":\"not set up in this test\"}");
        }

        var response = new HttpResponseMessage(reply.Status);
        if (reply.Json is not null)
        {
            response.Content = new StringContent(reply.Json, Encoding.UTF8, "application/json");
        }
        return response;
    }

    private static string Key(string method, string path) => $"{method.ToUpperInvariant()} {path}";
}
