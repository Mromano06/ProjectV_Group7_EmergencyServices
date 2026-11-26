using System.Collections.Concurrent;
using MyApplication.Api.Clients;
using MyApplication.Api.Models;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Tests;

/// <summary>
/// A pretend Public Transport service. Records what we sent and
/// can be told to "fail" so we can test what happens when Transit is down.
/// Used by both the unit tests and the API (integration) tests.
/// </summary>
public class FakeTransitClient : ITransitWarningClient
{
    public bool ShouldSucceed { get; set; } = true;

    private readonly ConcurrentQueue<TransitWarningNotice> _sent = new();
    public IReadOnlyCollection<TransitWarningNotice> Sent => _sent.ToArray();

    public Task<bool> SendWarningNoticeAsync(TransitWarningNotice notice)
    {
        _sent.Enqueue(notice);
        return Task.FromResult(ShouldSucceed);
    }

    private readonly ConcurrentQueue<TransitRoadClosure> _closures = new();
    /// <summary>Road closures we created, updated or cleared for our incidents (ES-FR-015, PT-FR-019), oldest first.</summary>
    public IReadOnlyCollection<TransitRoadClosure> SentClosures => _closures.ToArray();

    public Task<bool> SendRoadClosureAsync(TransitRoadClosure closure)
    {
        _closures.Enqueue(closure);
        return Task.FromResult(ShouldSucceed);
    }

    private readonly ConcurrentQueue<TransitRoadClosureResolution> _resolutions = new();
    /// <summary>Road closures we marked resolved (road open again), oldest first.</summary>
    public IReadOnlyCollection<TransitRoadClosureResolution> SentResolutions => _resolutions.ToArray();

    public Task<bool> ResolveRoadClosureAsync(TransitRoadClosureResolution resolution)
    {
        _resolutions.Enqueue(resolution);
        return Task.FromResult(ShouldSucceed);
    }
}