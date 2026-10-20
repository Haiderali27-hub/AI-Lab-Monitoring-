namespace Student_Desktop_App.Core.Monitoring;

/// <summary>One analysed webcam frame for the face / phone rules.</summary>
/// <param name="Faces">Faces the detector found; null = no face analysis for this frame (no model, no usable picture).</param>
/// <param name="PhoneScore">Best "cell phone" confidence (0 = none); null = the phone was not checked on this frame
/// (no model, or one of the frames the slower phone model skips): the phone state is left exactly as it is.</param>
public sealed record VisionObservation(DateTime AtUtc, IReadOnlyList<DetectedFace>? Faces, float? PhoneScore = null);

/// <summary>Tunable limits of the S8 webcam rules (raise times from the LabGuard analysis, wired this time).</summary>
public sealed record VisionThresholds
{
    public TimeSpan NoFaceRaiseAfter { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MultipleFacesRaiseAfter { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan LookingAwayRaiseAfter { get; init; } = TimeSpan.FromSeconds(3);
    /// <summary>With the phone model on every other frame this needs the phone in two consecutive checks.</summary>
    public TimeSpan PhoneRaiseAfter { get; init; } = TimeSpan.FromSeconds(1);
    /// <summary>A raised issue clears only after the condition was absent this long (no flapping).</summary>
    public TimeSpan ClearAfter { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Faces smaller than this fraction of the frame height are ignored: in a lab, the students two rows back
    /// are in view too; somebody close enough to help is not that small.</summary>
    public double MinFaceHeight { get; init; } = 0.1;

    /// <summary>Head turned further than this (degrees, either side, relative to the student's baseline) = looking away.</summary>
    public double MaxYaw { get; init; } = 30;
    /// <summary>Looking further down than this (degrees). Generous: in a programming lab the keyboard is down there.</summary>
    public double MaxPitchDown { get; init; } = 35;
    /// <summary>Looking further up than this (degrees).</summary>
    public double MaxPitchUp { get; init; } = 20;

    /// <summary>Single-face frames used to learn how this student normally faces this camera.</summary>
    public int CalibrationFrames { get; init; } = 8;
    /// <summary>Frames turned more than this are not used for the baseline (a student cannot "calibrate" while turned away).</summary>
    public double MaxBaselineYaw { get; init; } = 20;
    public double MaxBaselinePitch { get; init; } = 25;

    /// <summary>"Cell phone" confidence at or above this counts as a phone in view (YOLOX-Nano is small: lower than a big model's 0.5).</summary>
    public float PhoneMinScore { get; init; } = 0.45f;
}

/// <summary>
/// Module 5 (S8) — the face / phone state machine on top of the on-device models. Per frame it decides four
/// conditions and debounces each with <see cref="IssueDebounce"/>, so one event is sent per episode:
/// <list type="bullet">
/// <item><b>NoFace</b> — no face for 5 s (student left the seat, or turned fully away).</item>
/// <item><b>MultipleFaces</b> — two or more faces for 3 s (somebody helping). Faces count only above
/// <see cref="VisionThresholds.MinFaceHeight"/>.</item>
/// <item><b>LookingAway</b> — the one face turned beyond the yaw/pitch limits for 3 s, measured from
/// <see cref="HeadPose"/> relative to the student's own baseline (the median of the first calm frames), so a camera
/// mounted beside or above the screen does not make everybody "look away". A frame with no face neither starts nor
/// ends it: a head turned far enough is only found on some frames, and resetting the 3 s on every miss would let a
/// student turned to the neighbour's screen escape both this rule and NoFace.</item>
/// <item><b>PhoneDetected</b> — a cell phone in two consecutive phone checks, when the phone model is present.</item>
/// </list>
/// A frame without face analysis (no model, covered or unavailable camera — those have their own events) is "no
/// evidence": nothing is raised or cleared across it. The phone model runs on fewer frames than the face model, so a
/// frame without a phone check simply leaves the phone state as it is. Pure; <c>Services/Monitoring/WebcamMonitorService</c> feeds it.
/// </summary>
public sealed class VisionIssueDetector
{
    private readonly VisionThresholds _t;
    private readonly IssueDebounce _noFace, _multipleFaces, _lookingAway, _phone;
    private readonly List<HeadPoseEstimate> _calibration = new();

    public VisionIssueDetector(VisionThresholds? thresholds = null)
    {
        _t = thresholds ?? new VisionThresholds();
        _noFace = new IssueDebounce(CameraIssue.NoFace, _t.NoFaceRaiseAfter, _t.ClearAfter);
        _multipleFaces = new IssueDebounce(CameraIssue.MultipleFaces, _t.MultipleFacesRaiseAfter, _t.ClearAfter);
        _lookingAway = new IssueDebounce(CameraIssue.LookingAway, _t.LookingAwayRaiseAfter, _t.ClearAfter);
        _phone = new IssueDebounce(CameraIssue.PhoneDetected, _t.PhoneRaiseAfter, _t.ClearAfter);
    }

    /// <summary>The student's neutral pose once learned (null while calibrating).</summary>
    public HeadPoseEstimate? Baseline { get; private set; }

    /// <summary>Faces the rules counted in the last analysed frame (diagnostics).</summary>
    public int LastFaceCount { get; private set; }

    /// <summary>Heartbeat value: Ok, or the most serious active issue (MultipleFaces, Phone, NoFace, LookingAway).</summary>
    public string Status =>
        _multipleFaces.Active ? "MultipleFaces"
        : _phone.Active ? "Phone"
        : _noFace.Active ? "NoFace"
        : _lookingAway.Active ? "LookingAway"
        : "Ok";

    public bool IsActive(CameraIssue issue) => issue switch
    {
        CameraIssue.NoFace => _noFace.Active,
        CameraIssue.MultipleFaces => _multipleFaces.Active,
        CameraIssue.LookingAway => _lookingAway.Active,
        CameraIssue.PhoneDetected => _phone.Active,
        _ => false
    };

    /// <summary>The faces the rules count: big enough to be at this desk.</summary>
    public static IReadOnlyList<DetectedFace> CountedFaces(IReadOnlyList<DetectedFace> faces, VisionThresholds? thresholds = null)
    {
        var minHeight = (thresholds ?? new VisionThresholds()).MinFaceHeight;
        return faces.Where(f => f.Height >= minHeight).ToList();
    }

    /// <summary>How many faces the rules count (also the readiness "exactly one face" rule).</summary>
    public static int CountFaces(IReadOnlyList<DetectedFace> faces, VisionThresholds? thresholds = null) =>
        CountedFaces(faces, thresholds).Count;

    public IReadOnlyList<CameraTransition> Observe(VisionObservation o)
    {
        var transitions = new List<CameraTransition>();

        if (o.Faces is null)
        {
            _noFace.NoEvidence();
            _multipleFaces.NoEvidence();
            _lookingAway.NoEvidence();
        }
        else
        {
            var faces = CountedFaces(o.Faces, _t);
            LastFaceCount = faces.Count;
            Add(transitions, _noFace.Step(faces.Count == 0, o.AtUtc, "No face in front of the camera"));
            Add(transitions, _multipleFaces.Step(faces.Count >= 2, o.AtUtc, $"{faces.Count} faces in view"));

            if (faces.Count == 0)
            {
                // Nobody to judge: leave LookingAway exactly as it is (no Step: its timers keep running, nothing new is
                // decided). A face lost while turning away keeps counting towards LookingAway; a student who has left
                // the seat produces no "away" frame, so only NoFace is raised.
            }
            else if (faces.Count >= 2)
            {
                // Several people (that has its own event): not "looking away".
                Add(transitions, _lookingAway.Step(false, o.AtUtc, ""));
            }
            else if (HeadPose.Estimate(faces[0]) is { } raw)
            {
                var relative = Relative(raw);
                var away = Math.Abs(relative.YawDegrees) > _t.MaxYaw
                           || relative.PitchDegrees > _t.MaxPitchDown
                           || relative.PitchDegrees < -_t.MaxPitchUp;
                var direction = Math.Abs(relative.YawDegrees) > _t.MaxYaw ? "turned sideways"
                    : relative.PitchDegrees > 0 ? "looking down" : "looking up";
                Add(transitions, _lookingAway.Step(away, o.AtUtc,
                    $"Head {direction} (yaw {relative.YawDegrees:+0;-0;0}°, pitch {relative.PitchDegrees:+0;-0;0}°)"));
            }
            else
            {
                _lookingAway.NoEvidence();   // face too small for its landmarks to mean anything
            }
        }

        if (o.PhoneScore is float phone)
            Add(transitions, _phone.Step(phone >= _t.PhoneMinScore, o.AtUtc, $"Mobile phone in view (confidence {phone:0%})"));

        return transitions;
    }

    // Learn the baseline from the first calm single-face frames (median, so one odd frame does not skew it),
    // then report every pose relative to it. Until then the camera is assumed to face the student straight on.
    private HeadPoseEstimate Relative(HeadPoseEstimate raw)
    {
        if (Baseline is null && Math.Abs(raw.YawDegrees) <= _t.MaxBaselineYaw && Math.Abs(raw.PitchDegrees) <= _t.MaxBaselinePitch)
        {
            _calibration.Add(raw);
            if (_calibration.Count >= Math.Max(1, _t.CalibrationFrames))
            {
                Baseline = new HeadPoseEstimate(Median(_calibration.Select(p => p.YawDegrees)), Median(_calibration.Select(p => p.PitchDegrees)));
                _calibration.Clear();
            }
        }
        var b = Baseline ?? default;
        return new HeadPoseEstimate(raw.YawDegrees - b.YawDegrees, raw.PitchDegrees - b.PitchDegrees);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static void Add(List<CameraTransition> output, CameraTransition? transition)
    {
        if (transition is not null) output.Add(transition);
    }
}
