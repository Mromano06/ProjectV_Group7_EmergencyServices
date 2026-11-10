using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Clients;
using MyApplication.Api.Data;
using MyApplication.Api.Models;
using MyApplication.Api.Services;

var builder = WebApplication.CreateBuilder(args);

///<summary>
/// Main File for our Program
/// </summary>

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // When a request body fails validation (e.g. missing a [Required] field),
        // return our shared error shape { error, message, traceId } (SH-I03)
        // instead of ASP.NET's default format.
        options.InvalidModelStateResponseFactory = context =>
        {
            var message = string.Join(" ", context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage));
            return new BadRequestObjectResult(
                new ErrorResponse("validation_failed", message, context.HttpContext.TraceIdentifier));
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
    options.UseSqlServer(builder.Configuration.GetConnectionString("EmergencyDb")));

// Login / roles (SH-S03)
// Reads its settings from the "Authentication:Schemes:Bearer" section of appsettings.
// For local testing, `dotnet user-jwts create` fills that in for you
// When Registration's real login is ready, put their issuer/key in the same section.
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

// ----- Weather / public warnings -----
builder.Services.AddScoped<IWeatherWarningService, WeatherWarningService>();

// Makes random fake warnings in the background (settings: "WarningSimulator" in appsettings).
builder.Services.AddHostedService<SimulatedWarningGenerator>();

// Hospital services, Locations, Availibility, Size, etc.
builder.Services.AddScoped<IHospitalService, HospitalService>();

builder.Services.AddHttpClient<ITransitWarningClient, TransitWarningClient>(client =>
{
    var transitUrl = builder.Configuration["Services:TransitBaseUrl"];
    if (!string.IsNullOrWhiteSpace(transitUrl))
    {
        client.BaseAddress = new Uri(transitUrl);
    }
    client.Timeout = TimeSpan.FromSeconds(5); // SH-A07: don't hang if Transit is down
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger app for backend testing
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json",
        "Emergency Services API"));
}

app.UseHttpsRedirection();

app.UseAuthentication(); // must come BEFORE UseAuthorization
app.UseAuthorization();

app.MapControllers();

app.Run();
