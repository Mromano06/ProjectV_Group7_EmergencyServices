using System.ComponentModel.DataAnnotations;
namespace MyApplication.Api.Dtos;

// Matthew Romano - Nov 14th, 2026 - Extra/Util Dtos

// Creates an expense request
/// <summary>Proposed DTO; verify Budget team's JSON contract before enabling submission.</summary>
public sealed record ExpenseRequest(
    [Range(typeof(decimal), "0.01", "999999999.99")] decimal Amount,
    [Required, MinLength(1)] string Category,
    [Required, MinLength(1)] string Description,
    DateTime IncurredAt);

// Creates a staff change request
/// <summary>Proposed DTO; verify HR team's request contract.</summary>
public sealed record StaffChangeRequest(
    [Required] string Action, // Hire / Terminate
    [Required] string Profession,
    Guid? StaffId,
    string? Reason);

// Counts employees per profession
public sealed record ProfessionCount(string Profession, int Count);