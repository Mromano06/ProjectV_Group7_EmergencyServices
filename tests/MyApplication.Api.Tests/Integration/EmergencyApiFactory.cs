using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyApplication.Api.Clients;
using MyApplication.Api.Data;
using MyApplication.Api.Services;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// Starts the WHOLE Emergency Services API in memory for testing – the real Program.cs,
/// real controllers, real services, real routing and real [Authorize] rules – with only
/// these swaps so tests are fast and work on any laptop:
///
///   SQL Server            -> SQLite in memory (a real SQL database: transactions, unique
///                            indexes and check constraints all work). Fresh per test class.
///   JWT login             -> TestAuthHandler (pick a role with a header)
///   Public Transport      -> FakeTransitClient (records notices instead of sending them)
///   Budget / HR / Maintenance (through the gateway) -> FakeGateway (tests choose the replies)
///   Service-account token -> FakeServiceTokenProvider
///   Warning simulator     -> turned off (so random warnings don't appear mid-test)
/// </summary>
public class EmergencyApiFactory : WebApplicationFactory<Program>
{
    // An in-memory SQLite database lives as long as this connection stays open.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public EmergencyApiFactory()
    {
        _connection.Open();
    }

    /// <summary>The fake Transit service – tests can check what was sent, or make it "fail".</summary>
    public FakeTransitClient Transit { get; } = new();

    /// <summary>The fake gateway in front of Budget, HR and Maintenance.</summary>
    public FakeGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // ---- Database: SQL Server -> SQLite in memory ----
            services.RemoveAll<EmergencyDbContext>();
            services.RemoveAll<DbContextOptions<EmergencyDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<EmergencyDbContext>>();
            var options = new DbContextOptionsBuilder<EmergencyDbContext>().UseSqlite(_connection).Options;
            services.AddSingleton(options);
            services.AddScoped<EmergencyDbContext>(_ => new SqliteTestDbContext(options));

            // ---- Turn off the random warning simulator ----
            var simulator = services.SingleOrDefault(d =>
                d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType == typeof(SimulatedWarningGenerator));
            if (simulator != null)
            {
                services.Remove(simulator);
            }

            // ---- Public Transport -> fake ----
            services.RemoveAll<ITransitWarningClient>();
            services.AddSingleton<ITransitWarningClient>(Transit);

            // ---- Other teams' services (through the gateway) -> fake gateway ----
            services.RemoveAll<IServiceTokenProvider>();
            services.AddSingleton<IServiceTokenProvider, FakeServiceTokenProvider>();

            services.AddHttpClient<BudgetFinanceClient>(c => c.BaseAddress = new Uri(FakeGateway.BaseUrl))
                .ConfigurePrimaryHttpMessageHandler(() => Gateway);
            services.AddHttpClient<StaffHrClient>(c => c.BaseAddress = new Uri(FakeGateway.BaseUrl))
                .ConfigurePrimaryHttpMessageHandler(() => Gateway);
            services.AddHttpClient<RoadClosureClient>(c => c.BaseAddress = new Uri(FakeGateway.BaseUrl))
                .ConfigurePrimaryHttpMessageHandler(() => Gateway);

            // ---- Login: JWT -> test login by role header ----
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Build all the tables from the team's real model.
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<EmergencyDbContext>().Database.EnsureCreated();

        return host;
    }

    /// <summary>
    /// An HTTP client logged in with the given role (e.g. Roles.Admin),
    /// or not logged in at all if role is null.
    /// </summary>
    public HttpClient CreateClientAs(string? role)
    {
        var client = CreateClient();
        if (role != null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        }
        return client;
    }

    /// <summary>An HTTP client logged in as another city service's account, e.g. "security".</summary>
    public HttpClient CreateServiceClient(string serviceName)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.ClaimsHeader, $"service={serviceName}");
        return client;
    }

    /// <summary>
    /// Puts data straight into the test database – for things the API has no endpoint to
    /// create (e.g. hospitals).
    /// </summary>
    public async Task SeedAsync(Action<EmergencyDbContext> add)
    {
        _ = CreateClient(); // make sure the API (and database) has started
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmergencyDbContext>();
        add(db);
        await db.SaveChangesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
