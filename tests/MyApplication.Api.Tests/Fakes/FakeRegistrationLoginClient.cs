using MyApplication.Api.Clients;
using MyApplication.Api.Integration;

namespace MyApplication.Api.Tests;

/// <summary>
/// A pretend Registration login. Tests add made-up accounts (never real ones) and can make it "go down".
/// Records the usernames it was asked about, so tests can check passwords are passed on correctly.
/// </summary>
public sealed class FakeRegistrationLoginClient : IRegistrationLoginClient
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (string Password, RegistrationLogin Login)> _accounts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _attempts = [];

    /// <summary>True = Registration can't be reached (throws like the real client does).</summary>
    public bool Down { get; set; }

    /// <summary>Every username we were asked to check, oldest first.</summary>
    public IReadOnlyList<string> Attempts { get { lock (_lock) return _attempts.ToList(); } }

    public void AddAccount(string username, string password, string role,
        string? displayName = null, DateTime? expiresAtUtc = null) =>
        AddAccount(username, password, [role], displayName, expiresAtUtc);

    public void AddAccount(string username, string password, IReadOnlyList<string> roles,
        string? displayName = null, DateTime? expiresAtUtc = null)
    {
        lock (_lock)
            _accounts[username] = (password, new RegistrationLogin(
                Guid.NewGuid().ToString(), username, displayName ?? username, roles, expiresAtUtc));
    }

    public Task<RegistrationLogin?> AuthenticateAsync(string username, string password, CancellationToken ct)
    {
        lock (_lock)
        {
            _attempts.Add(username);
            if (Down) throw new ExternalServiceException("Could not reach Registration.", 503);
            return Task.FromResult(
                _accounts.TryGetValue(username, out var a) && a.Password == password ? a.Login : null);
        }
    }
}
