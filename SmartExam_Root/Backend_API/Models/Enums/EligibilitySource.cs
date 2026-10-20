namespace Backend_API.Models.Enums;

/// <summary>Who decided an exam assignment's eligibility (Module 9). Stored as a string.</summary>
public enum EligibilitySource
{
    Attendance,   // computed by "Recalculate from attendance"
    Manual        // set by a teacher on the Eligibility page — survives recalculation
}
