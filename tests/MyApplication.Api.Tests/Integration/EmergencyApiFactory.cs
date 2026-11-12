using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
///   SQL Server        -> an in-memory database (fresh for each test class)
///   JWT login         -> TestAuthHandler (pick a role with a header)
///   Public Transport  -> FakeTransitClient (records notices instead of sending them)
///   Warning simulator -> turned off (so random warnings don't appear mid-test)
///
/// Teammates: if your feature calls another city service, swap its client here the same way
/// Transit is swapped, so tests never depend on another team's server being up.
/// </summary>
public class EmergencyApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "EmergencyTests-" + Guid.NewGuid();

    /// <summary>The fake Transit service – tests can check what was sent, or make it "fail".</summary>
    public FakeTransitClient Transit { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // ---- Database: SQL Server -> in-memory ----
            services.RemoveAll<DbContextOptions<EmergencyDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<EmergencyDbContext>>();
            services.AddDbContext<EmergencyDbContext>(options => options.UseInMemoryDatabase(_databaseName));

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

            // ---- Login: JWT -> test login by role header ----
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
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
}
