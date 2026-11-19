namespace MyApplication.Api.Enums;

/// <summary>
/// Where a warning came from (class diagram: WarningSource).
/// Manual    = typed in by an Emergency Admin.
/// Simulated = made up by the SimulatedWarningGenerator so there is always demo data.
/// </summary>
public enum WarningSource
{
    Manual,
    Simulated
}