using System.Globalization;

namespace Backend_API.Services;

/// <summary>Outcome of one attendance-based eligibility decision. <c>IsEligible</c> null = not computed.</summary>
public sealed record EligibilityDecision(bool? IsEligible, double? Percent, string Note)
{
    public bool Computed => IsEligible.HasValue;
}

/// <summary>
/// Module 9 — pure attendance → eligibility rule (no DB), so the boundaries are unit-tested.
/// The percentage is compared UNROUNDED against the minimum (74.9 % &lt; 75 %), and only rounded
/// for the note shown to the teacher.
/// </summary>
public static class EligibilityCalculator
{
    public const string NoRecordNote = "No attendance record — not computed";
    public const string NoClassesNote = "No attendance classes recorded — not computed";

    public static double? Percent(int attended, int total) =>
        total <= 0 ? null : attended * 100.0 / total;

    /// <param name="hasRecord">false when the student has no attendance row in the exam's section.</param>
    public static EligibilityDecision Decide(bool hasRecord, int attended, int total, double minPercent)
    {
        if (!hasRecord) return new EligibilityDecision(null, null, NoRecordNote);
        if (total <= 0) return new EligibilityDecision(null, null, NoClassesNote);

        var pct = Percent(attended, total)!.Value;
        var eligible = pct >= minPercent;
        var note = string.Format(CultureInfo.InvariantCulture,
            eligible ? "Attendance {0:0.##}% ≥ {1:0.##}%" : "Attendance {0:0.##}% < {1:0.##}%", pct, minPercent);
        return new EligibilityDecision(eligible, pct, note);
    }
}
