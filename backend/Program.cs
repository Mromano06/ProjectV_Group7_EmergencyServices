
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

// Login / roles (SH-S03)
// Reads its settings from the "Authentication:Schemes:Bearer" section of appsettings.
// For local testing, `dotnet user-jwts create` fills that in for you
// When Registration's real login is ready, put their issuer/key in the same section.
builder.Services.AddAuthentication().AddJwtBearer();

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


// TODO: Remove once done
// Adds tokens automaticall for dev
builder.Services.AddScoped<IServiceTokenProvider, DevelopmentServiceTokenProvider>();


// Shared API gateway for external services
var gateway = builder.Configuration["Services:GatewayBaseUrl"];


// Transit integration – warning notices go through the API gateway (SH-A03)
// with our service-account token (SH-S07), like the other integrations.
builder.Services.AddHttpClient<ITransitWarningClient, TransitWarningClient>(http =>
{
    if (!string.IsNullOrWhiteSpace(gateway))
    {
        http.BaseAddress = new Uri(gateway);
    }

    http.Timeout = TimeSpan.FromSeconds(5);
});


// Budget / Finance integration
builder.Services.AddHttpClient<BudgetFinanceClient>(http =>
{
    if (!string.IsNullOrWhiteSpace(gateway))
    {
        http.BaseAddress = new Uri(gateway);
    }

    http.Timeout = TimeSpan.FromSeconds(5);
});


// HR / Staff integration
builder.Services.AddHttpClient<StaffHrClient>(http =>
{
    if (!string.IsNullOrWhiteSpace(gateway))
    {
        http.BaseAddress = new Uri(gateway);
    }

    http.Timeout = TimeSpan.FromSeconds(5);
});


// Maintenance / Road Closures integration
builder.Services.AddHttpClient<RoadClosureClient>(http =>
{
    if (!string.IsNullOrWhiteSpace(gateway))
    {
        http.BaseAddress = new Uri(gateway);
    }

    http.Timeout = TimeSpan.FromSeconds(5);
});


// Build the application after ALL services have been registered.
var app = builder.Build();


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

app.MapControllers();

app.Run();

public partial class Program { }
