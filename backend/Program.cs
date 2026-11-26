using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Clients;
using MyApplication.Api.Data;
using MyApplication.Api.Models;
using MyApplication.Api.Services;
using System.Text.Json.Serialization;
using MyApplication.Api.Dtos;
using MyApplication.Api.Health;
using MyApplication.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

/// <summary>
/// Main File for our Program
/// </summary>


// When a request body fails validation (e.g. missing a [Required] field),
// return our shared error shape { error, message, traceId } (SH-I03)
// instead of ASP.NET's default format.
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var message = string.Join(" ",
                context.ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));

            return new BadRequestObjectResult(
                new ErrorResponse(
                    "validation_failed",
                    message,
                    context.HttpContext.TraceIdentifier));
        };
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    // Tell Swagger that our API uses JWT authentication.
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();

        document.Components.SecuritySchemes =
            new Dictionary<string, IOpenApiSecurityScheme>
            {
                ["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header
                }
            };

        return Task.CompletedTask;
    });

    // Mark endpoints requiring authorization.
    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var requiresAuth = context.Description.ActionDescriptor
            .EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        if (requiresAuth)
        {
            operation.Security ??= [];

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(
                    "Bearer", context.Document)] = []
            });
        }

        return Task.CompletedTask;
    });
});


// Database (SH-D01)
// Connection string comes from appsettings / environment variables (SH-A06).
builder.Services.AddDbContext<EmergencyDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("EmergencyDb")));

// Login / roles (SH-S01, SH-S03): every request's JWT is validated.
// Tokens are made by our own sign-in (AuthController, after Registration checks the password) or, for
// local testing, by `dotnet user-jwts create`. Both use the "Authentication:Schemes:Bearer" settings.
// Optional: Registration's own tokens are also accepted if they ever share a key ("Registration" settings,
// Auth/RegistrationTokens.cs). Not needed for the console login.
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
    o => RegistrationTokens.Accept(o.TokenValidationParameters, builder.Configuration));

// Registration's plain roles (Manager, Dispatcher, User) -> our emergency:* roles. See appsettings "RoleMapping".
builder.Services.AddTransient<IClaimsTransformation, RegistrationRoleMapper>();

// Console sign-in (SH-S01), as Registration's Auth Guide says: POST /api/emergency/v1/auth/login checks the
// username and password with Registration (authenticate, then validate), then we give the console our own
// token, signed with the dev/JWT_SIGNING_KEY settings above. See Controllers/AuthController.cs.
builder.Services.AddSingleton<EmergencyTokenIssuer>();
builder.Services.AddLoginRateLimit(); // max 20 sign-in tries a minute (see Auth/LoginRateLimit.cs)

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("EmergencyIncidentCreate", policy =>
        policy.RequireAuthenticatedUser().RequireAssertion(context =>
            context.User.IsInRole("emergency:Admin") ||
            context.User.IsInRole("emergency:Dispatcher") ||
            // Provisional service credential mapping: Registration must confirm it.
            context.User.HasClaim("service", "security")));
});


// ----- Weather / public warnings -----
builder.Services.AddScoped<IWeatherWarningService, WeatherWarningService>();

// Makes random fake warnings in the background (settings: "WarningSimulator" in appsettings).
builder.Services.AddHostedService<SimulatedWarningGenerator>();

// Hospital services, Locations, Availibility, Size, etc.
builder.Services.AddScoped<IHospitalService, HospitalService>();

// Units details, station, and availibility
builder.Services.AddScoped<IUnitService, UnitService>();

// Station details, location, and availibility
builder.Services.AddScoped<IStationService, StationService>();

// Dispatch details, inculding units, stations, and incidents
builder.Services.AddScoped<IDispatchService, DispatchService>();

// Incident details, type, severity, location
builder.Services.AddScoped<IIncidentService, IncidentService>();

// Stat details, deaths, incidents, time, location
builder.Services.AddScoped<IStatsService, StatsService>();

// Health check for GET /health (ES-NFR-001, SH-A05): 200 when the API and its database are up, 503 if not.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");




// Where other teams' services live (SH-A03, SH-A06).
// Normally every call goes through the shared API gateway: Services__GatewayBaseUrl.
// Until the class gateway exists, a team's own URL can be set instead and wins over the gateway, e.g.
//   Services__BudgetBaseUrl=https://<budget-app>.azurewebsites.net/
// Teams: Transit (warnings, incident notices, road closures), Budget, Hr, Registration (login).
string? ServiceUrl(string team)
{
    var url = builder.Configuration[$"Services:{team}BaseUrl"];
    if (string.IsNullOrWhiteSpace(url)) url = builder.Configuration["Services:GatewayBaseUrl"];
    if (string.IsNullOrWhiteSpace(url)) return null;
    return url.EndsWith('/') ? url : url + "/"; // so "api/budget/v1/..." is added after any path in the URL
}

void UseService(HttpClient http, string team)
{
    var url = ServiceUrl(team);
    if (url != null) http.BaseAddress = new Uri(url);
    http.Timeout = TimeSpan.FromSeconds(5); // SH-A07: give up after 5 s instead of hanging
}

// Public Transport: weather notices, road-restricting incidents (ES-FR-015) and road closures (ES-FR-010)
builder.Services.AddHttpClient<ITransitWarningClient, TransitWarningClient>(http => UseService(http, "Transit"));
builder.Services.AddHttpClient<RoadClosureClient>(http => UseService(http, "Transit"));

// Budget / Finance (ES-FR-025)
builder.Services.AddHttpClient<BudgetFinanceClient>(http => UseService(http, "Budget"));

// HR / Staff (ES-FR-026)
builder.Services.AddHttpClient<StaffHrClient>(http => UseService(http, "Hr"));

// Registration: checks usernames and passwords for the console login (SH-S01).
// Their live API unless Services__RegistrationBaseUrl (REGISTRATION_URL in .env) says otherwise.
builder.Services.AddHttpClient<IRegistrationLoginClient, RegistrationLoginClient>(http =>
{
    UseService(http, "Registration");
    http.BaseAddress ??= new Uri(RegistrationLoginClient.DefaultBaseUrl);
});

// Our Registration service account (SH-S07): the Bearer token sent to Transit, Budget and HR.
// Logs in with ServiceAccount__Username / ServiceAccount__Password and caches the token until it expires
// (or uses ServiceAccount__AccessToken if one is pasted in). One shared instance so the token is shared too.
builder.Services.AddHttpClient(RegistrationServiceTokenProvider.HttpClientName, http =>
{
    UseService(http, "Registration");
    http.BaseAddress ??= new Uri(RegistrationLoginClient.DefaultBaseUrl);
});
builder.Services.AddSingleton<IServiceTokenProvider, RegistrationServiceTokenProvider>();

builder.Services.AddScoped<IPatientTransportService, PatientTransportService>();

// Build the application after ALL services have been registered.
var app = builder.Build();

// Say in the log whether Registration's tokens are accepted (stops here if their public key is invalid).
app.Logger.LogInformation("{RegistrationTokens}", RegistrationTokens.Describe(app.Configuration));


// In Docker, create/update the database tables on startup (set Database__MigrateOnStartup=true).
// Retries because SQL Server can take a while to be ready.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();

    var db = scope.ServiceProvider
        .GetRequiredService<EmergencyDbContext>();

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            db.Database.Migrate();
            break;
        }
        catch (Exception ex) when (attempt < 10)
        {
            app.Logger.LogWarning(
                ex,
                "Database not ready (attempt {Attempt}/10), retrying in 5s",
                attempt);

            Thread.Sleep(5000);
        }
    }
}

// Demo data for a fresh database (SH-D03): stations, units and hospitals.
// Only fills EMPTY tables, so it is safe on every startup. Off unless Database__SeedOnStartup=true.
if (app.Configuration.GetValue<bool>("Database:SeedOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EmergencyDbContext>();
    var added = await SeedData.SeedAsync(db);
    app.Logger.LogInformation("Seed data: {Added} rows added (0 = already seeded).", added);
}


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger app for backend testing
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "Emergency Services API"));
}

app.UseHttpsRedirection();

app.UseAuthentication(); // must come BEFORE UseAuthorization
app.UseAuthorization();
app.UseRateLimiter(); // for the sign-in limit on /auth/login

app.MapControllers();

// GET /health – open to everyone (no login) so Docker, the API gateway and other teams can check us.
// Also at /api/emergency/v1/health so it can be reached through the gateway's /api/emergency/... route.
var healthOptions = new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync };
app.MapHealthChecks("/health", healthOptions).AllowAnonymous();
app.MapHealthChecks("/api/emergency/v1/health", healthOptions).AllowAnonymous();

app.Run();

public partial class Program { }