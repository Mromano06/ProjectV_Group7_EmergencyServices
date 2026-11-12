using System.Collections.Concurrent;
using MyApplication.Api.Clients;
using MyApplication.Api.Models;

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
}
