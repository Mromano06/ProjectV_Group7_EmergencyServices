namespace MyApplication.Api.Enums;

//sebastian solorzano - nov 21, triage level enum for patient transport

/// <summary>
/// represents the triage level for patients being transported.
/// follows CTAS scale used by Canada
/// req. ES-FR-014
/// </summary>
public enum TriageLevel
{
    Resuscitation = 1,
    Emergency = 2,
    Urgent = 3,
    LessUrgent = 4,
    NonUrgent = 5
}