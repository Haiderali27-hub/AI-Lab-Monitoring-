namespace Student_Desktop_App.Core.Monitoring;

/// <summary>
/// A point in normalised frame coordinates: both axes in units of the frame's <b>height</b> (Y runs 0..1, X runs
/// 0..width/height), origin top-left. One unit for both axes keeps angles and distances true, whatever the camera's
/// resolution or aspect ratio.
/// </summary>
public readonly record struct FacePoint(float X, float Y);

/// <summary>
/// One face found by the on-device face detector (YuNet), in normalised frame coordinates (see <see cref="FacePoint"/>)
/// so the rules do not depend on the camera resolution. <see cref="Landmarks"/> are YuNet's five points in its order:
/// the person's right eye (left in the picture), left eye, nose tip, right mouth corner, left mouth corner.
/// </summary>
public sealed record DetectedFace(float X, float Y, float Width, float Height, float Score, IReadOnlyList<FacePoint> Landmarks)
{
    public FacePoint RightEye => Landmarks[0];
    public FacePoint LeftEye => Landmarks[1];
    public FacePoint Nose => Landmarks[2];
    public FacePoint RightMouth => Landmarks[3];
    public FacePoint LeftMouth => Landmarks[4];
    public bool HasLandmarks => Landmarks.Count >= 5;
}

/// <summary>What one frame's analysis found. Either part is null when its model is not loaded (that check is off).</summary>
/// <param name="Faces">Faces above the detector's confidence floor; null = no face model.</param>
/// <param name="PhoneScore">Highest "cell phone" confidence in the frame (0 = none); null = not checked on this frame.</param>
public sealed record VisionResult(IReadOnlyList<DetectedFace>? Faces, float? PhoneScore);

/// <summary>Head rotation relative to looking straight at the camera, in degrees. Yaw: + = nose towards the picture's
/// right. Pitch: + = looking down, − = looking up.</summary>
public readonly record struct HeadPoseEstimate(double YawDegrees, double PitchDegrees);

/// <summary>
/// Module 5 (S8) — approximate head pose from YuNet's five landmarks, with no camera calibration and no solver.
/// Idea (LabGuard used solvePnP on 6 FaceMesh points; this is a closed-form version for the same generic 3D face):
/// <list type="number">
/// <item>Roll: the face's mid-line runs from the eye midpoint to the mouth midpoint, both about equally deep, so it
/// stays upright under yaw and pitch and only tilts with roll. The points are turned so that it is vertical.</item>
/// <item>Yaw: the eyes sit about <c>D</c> behind the nose tip, so turning the head by θ moves the nose tip sideways from
/// the eye midpoint by D·sin θ while the eyes' horizontal distance shrinks to 2e·cos θ (pitch does not change
/// horizontal positions): <c>tan θ = offset / eye distance × 2e / D</c>.</item>
/// <item>Pitch: where the nose tip sits between the eye line and the mouth line, solved for the angle that moves it
/// there (the depth term shrinks by cos θ when the head is also turned).</item>
/// </list>
/// Exact on synthetic landmarks to within 2° over yaw ±45°, pitch ±30°, roll ±20° (M5-U-19); on real landmarks the
/// error is the detector's, a few degrees for a face at a lab desk. The monitoring rule also subtracts each student's
/// own baseline, which absorbs a camera mounted above or beside the screen. Pure.
/// </summary>
public static class HeadPose
{
    // Generic adult face, nose tip at the origin, arbitrary units (the classic 3D head-pose model's proportions).
    private const double EyeHalfDistance = 150;   // eye centre to the face's mid-line (e)
    private const double EyeDepth = 135;          // eye plane behind the nose tip (D)
    private const double EyeHeight = 170;         // eye line above the nose tip
    private const double MouthDrop = 150;         // mouth line below the nose tip
    private const double MouthDepth = 125;        // mouth plane behind the nose tip

    /// <summary>Faces whose eyes are closer than this (in frame heights, ≈ 10 px at 240 rows) are too small to judge.</summary>
    public const double MinEyeDistance = 0.04;

    /// <summary>The pose, or null when the landmarks are too small or degenerate to say anything.</summary>
    public static HeadPoseEstimate? Estimate(DetectedFace face, double minEyeDistance = MinEyeDistance)
    {
        if (!face.HasLandmarks) return null;
        double eyeMidX = (face.RightEye.X + face.LeftEye.X) / 2.0, eyeMidY = (face.RightEye.Y + face.LeftEye.Y) / 2.0;
        double mouthMidX = (face.RightMouth.X + face.LeftMouth.X) / 2.0, mouthMidY = (face.RightMouth.Y + face.LeftMouth.Y) / 2.0;
        if (Distance(face.RightEye.X, face.RightEye.Y, face.LeftEye.X, face.LeftEye.Y) < minEyeDistance) return null;

        // 1. Undo roll: turn about the eye midpoint until the eye→mouth mid-line points straight down.
        var roll = Math.Atan2(mouthMidX - eyeMidX, mouthMidY - eyeMidY);
        double cos = Math.Cos(roll), sin = Math.Sin(roll);
        (double X, double Y) Upright(double x, double y)
        {
            double dx = x - eyeMidX, dy = y - eyeMidY;
            return (dx * cos - dy * sin, dx * sin + dy * cos);
        }
        var rightEye = Upright(face.RightEye.X, face.RightEye.Y);
        var leftEye = Upright(face.LeftEye.X, face.LeftEye.Y);
        var nose = Upright(face.Nose.X, face.Nose.Y);
        var mouth = Upright(mouthMidX, mouthMidY);

        var eyeSpan = leftEye.X - rightEye.X;
        // Eyes swapped or on top of each other, or the mouth not below the eyes: landmarks that make no sense.
        if (eyeSpan <= minEyeDistance / 2 || mouth.Y <= eyeSpan * 0.2) return null;

        // 2. Yaw from the nose's sideways offset.
        var yaw = Math.Atan(nose.X / eyeSpan * 2 * EyeHalfDistance / EyeDepth);

        // 3. Pitch: rotating the generic face by φ (after the turn θ) gives
        //    t = (H·cos φ + Dₑ·cos θ·sin φ) / ((H+M)·cos φ + (Dₑ−Dₘ)·cos θ·sin φ); solved for φ.
        var t = nose.Y / mouth.Y;
        var tanPitch = (t * (EyeHeight + MouthDrop) - EyeHeight) / ((EyeDepth - t * (EyeDepth - MouthDepth)) * Math.Cos(yaw));

        return new HeadPoseEstimate(yaw * 180 / Math.PI, Math.Atan(tanPitch) * 180 / Math.PI);
    }

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

    /// <summary>
    /// Landmarks of the generic face turned by the given yaw, pitch and roll and projected (orthographically) around
    /// (<paramref name="centreX"/>, <paramref name="centreY"/>) — the inverse of <see cref="Estimate"/>, for tests and
    /// synthetic inputs.
    /// </summary>
    public static DetectedFace Synthesize(double yawDegrees, double pitchDegrees, double rollDegrees = 0,
        float centreX = 0.65f, float centreY = 0.5f, float eyeDistance = 0.12f, float score = 0.95f)
    {
        double yaw = yawDegrees * Math.PI / 180, pitch = pitchDegrees * Math.PI / 180, roll = rollDegrees * Math.PI / 180;
        var scale = eyeDistance / (2 * EyeHalfDistance);
        // Model points (x right in the picture, y down, z towards the camera), nose tip at the origin.
        var model = new (double X, double Y, double Z)[]
        {
            (-EyeHalfDistance, -EyeHeight, -EyeDepth), (EyeHalfDistance, -EyeHeight, -EyeDepth), (0, 0, 0),
            (-EyeHalfDistance * 0.8, MouthDrop, -MouthDepth), (EyeHalfDistance * 0.8, MouthDrop, -MouthDepth)
        };
        var points = model.Select(p =>
        {
            // Yaw about the vertical axis (nose moves right for + yaw), pitch about the horizontal one (nose moves
            // down for + pitch), then roll in the picture plane.
            var x1 = p.X * Math.Cos(yaw) + p.Z * Math.Sin(yaw);
            var z1 = -p.X * Math.Sin(yaw) + p.Z * Math.Cos(yaw);
            var y2 = p.Y * Math.Cos(pitch) + z1 * Math.Sin(pitch);
            var xr = x1 * Math.Cos(roll) - y2 * Math.Sin(roll);
            var yr = x1 * Math.Sin(roll) + y2 * Math.Cos(roll);
            return new FacePoint((float)(centreX + xr * scale), (float)(centreY + yr * scale));
        }).ToList();
        var width = eyeDistance * 2.2f;
        var height = width * 1.25f;
        return new DetectedFace(centreX - width / 2, centreY - height / 2, width, height, score, points);
    }
}
