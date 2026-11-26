using System.ComponentModel.DataAnnotations;
namespace MyApplication.Api.Dtos;

// Matthew Romano - Nov 14th, 2026 - Extra/Util Dtos

// ---------- Budget & Finance (ES-FR-025, SH-C07/C08, BF-07) – contract confirmed Nov 25 ----------

/// <summary>
/// What a user sends to POST /api/emergency/v1/budget/expenses. No department code:
/// our API always sends "EMS", so nobody can charge another department's budget.
/// </summary>
public sealed record EmergencyExpenseRequest(
    [Range(typeof(decimal), "0.01", "999999999.99")] decimal Amount,
    [Required, MinLength(1), MaxLength(500)] string Description);

/// <summary>What we send to Budget: POST /api/budget/v1/expenses.</summary>
public sealed record ExpenseRequest(string DepartmentCode, decimal Amount, string Description);

/// <summary>What a user sends to POST /api/emergency/v1/budget/funding-requests (ask for more money).</summary>
public sealed record FundingRequest(
    [Range(typeof(decimal), "0.01", "999999999.99")] decimal Amount,
    [Required, MinLength(1), MaxLength(1000)] string Reason,
    bool Urgent);

/// <summary>What we send to Budget: POST /api/budget/v1/funding-requests.</summary>
public sealed record BudgetFundingRequest(string DepartmentCode, decimal Amount, string Reason, bool Urgent);


// Creates a staff change request
/// <summary>Proposed DTO; verify HR team's request contract.</summary>
public sealed record StaffChangeRequest(
    [Required] string Action, // Hire / Terminate
    [Required] string Profession,
    Guid? StaffId,
    string? Reason);

// Counts employees per profession
public sealed record ProfessionCount(string Profession, int Count);