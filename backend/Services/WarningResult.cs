using MyApplication.Api.Models;

namespace MyApplication.Api.Services;

/// <summary>
/// What a service method hands back to the controller: either the warning,
/// or a reason it failed. The controller turns the reason into the right
/// HTTP status code (ES-NFR-003 / SH-I03).
/// </summary>
public enum WarningOutcome
{
    Ok,
    NotFound,   // -> 404
    Invalid,    // -> 400
    Conflict    // -> 409
}

public class WarningResult
{
    public WarningOutcome Outcome { get; private init; }
    public WeatherWarning? Warning { get; private init; }
    public string ErrorCode { get; private init; } = string.Empty;
    public string Message { get; private init; } = string.Empty;

    public static WarningResult Ok(WeatherWarning w) =>
        new() { Outcome = WarningOutcome.Ok, Warning = w };

    public static WarningResult NotFound(Guid id) =>
        new() { Outcome = WarningOutcome.NotFound, ErrorCode = "warning_not_found", Message = $"No warning with id {id}." };

    public static WarningResult Invalid(string code, string message) =>
        new() { Outcome = WarningOutcome.Invalid, ErrorCode = code, Message = message };

    public static WarningResult Conflict(string code, string message) =>
        new() { Outcome = WarningOutcome.Conflict, ErrorCode = code, Message = message };
}
