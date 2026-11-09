namespace MyApplication.Api.Enums;

/// <summary>
/// Status of a warning. This is NOT saved in the database – it is worked out
/// from StartsAt / EndsAt every time we read a warning. That is how warnings
/// "expire automatically"
/// once the clock passes EndsAt the warning simply stops being Active.
/// </summary>
public enum WarningStatus
{
    Scheduled, // StartsAt is still in the future
    Active,    // now is between StartsAt and EndsAt
    Ended      // EndsAt has passed (ran out, or an Admin ended it early)
}
