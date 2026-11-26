using System.ComponentModel.DataAnnotations;

namespace MyApplication.Api.Dtos;

/// <summary>POST /api/emergency/v1/auth/login: the username and password for the city account (Registration).</summary>
public record LoginRequest(
    [Required, MinLength(1), MaxLength(100)] string Username,
    [Required, MinLength(1), MaxLength(200)] string Password);

/// <summary>
/// A successful login. The console sends AccessToken as "Authorization: Bearer ..." on every call.
/// Role is our Emergency role (e.g. emergency:Admin); RegistrationRole is Registration's role(s), e.g. "Manager".
/// </summary>
public record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    string Name,
    string Username,
    string Role,
    string RegistrationRole);
