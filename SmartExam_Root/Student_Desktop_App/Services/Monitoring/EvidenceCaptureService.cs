using Student_Desktop_App.Core.Monitoring;

namespace Student_Desktop_App.Services.Monitoring;

/// <summary>
/// Module 5 — screen captures during an exam: all monitors, JPEG q60 at ≤ 1280 px, on a random 30–90 s schedule
/// (<see cref="CaptureSchedule"/>, mean from <c>Exam.ScreenshotIntervalSeconds</c>) and immediately when a violation
/// is reported (at most one per 15 s). Every capture — and every webcam snapshot handed in by
/// <see cref="WebcamMonitorService"/> — is written to the encrypted <see cref="CaptureSpool"/> first and uploaded
/// from there, so an outage delays evidence instead of losing it. Capturing and encoding run off the UI and hook
/// threads.
/// </summary>
public sealed class EvidenceCaptureService
{
    private readonly ApiClient _api;
    private readonly Func<string> _accessToken;
    private readonly Guid _sessionId;
    private readonly CaptureSpool _spool;
    private readonly CaptureSchedule _schedule;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _uploading = new(1, 1);
    private int _capturing;

    /// <param name="accessToken">Fallback token; ApiClient sends the current (refreshed) session token.</param>
    public EvidenceCaptureService(ApiClient api, Func<string> accessToken, Guid sessionId, CaptureSpool spool, int screenshotMeanSeconds)
    {
        _api = api;
        _accessToken = accessToken;
        _sessionId = sessionId;
        _spool = spool;
        _schedule = new CaptureSchedule(screenshotMeanSeconds, () => DateTime.UtcNow, Random.Shared.NextDouble);
    }

    /// <summary>A capture failed (locked desktop, GDI error) — reported once per failure streak.</summary>
    public event Action<string>? CaptureFailed;

    public int Pending => _spool.Count;

    public void UpdateInterval(int meanSeconds)
    {
        lock (_gate) _schedule.UpdateMean(meanSeconds);
    }

    /// <summary>Called about once a second: takes the interval capture when it is due.</summary>
    public void Tick()
    {
        bool due;
        lock (_gate) due = _schedule.TakeIntervalDue();
        if (due) CaptureScreenInBackground("Interval", null);
    }

    /// <summary>A violation was reported: capture the screen now (rate-limited).</summary>
    public void CaptureForViolation(string reason)
    {
        bool allowed;
        lock (_gate) allowed = _schedule.TryClaimViolationCapture();
        if (allowed) CaptureScreenInBackground("Violation", reason);
    }

    /// <summary>Spools a webcam snapshot for upload. A spool that cannot be written (disk full, folder locked) loses
    /// this snapshot and reports it; it never throws into the webcam thread.</summary>
    public void AddWebcamSnapshot(byte[] jpeg, string trigger, string? reason)
    {
        try
        {
            _spool.Add(new CaptureInfo(Guid.NewGuid(), "Webcam", trigger, reason, DateTime.UtcNow), jpeg);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            CaptureFailed?.Invoke($"Webcam snapshot could not be stored: {ex.Message}");
        }
    }

    private void CaptureScreenInBackground(string trigger, string? reason)
    {
        // One capture at a time; a second request while one runs is simply covered by it.
        if (Interlocked.Exchange(ref _capturing, 1) == 1) return;
        var capturedAt = DateTime.UtcNow;
        _ = Task.Run(() =>
        {
            try
            {
                var jpeg = ScreenCapture.CaptureAllScreensJpeg();
                if (jpeg is null)
                {
                    CaptureFailed?.Invoke("The screen could not be captured.");
                    return;
                }
                _spool.Add(new CaptureInfo(Guid.NewGuid(), "Screen", trigger, reason, capturedAt), jpeg);
            }
            catch (Exception ex)
            {
                CaptureFailed?.Invoke(ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _capturing, 0);
            }
        });
    }

    /// <summary>
    /// Uploads spooled captures, oldest first. Stops at the first one the network could not take (it is retried on
    /// the next call); captures the server refused for good (session over, not wanted) are dropped.
    /// Returns false when the server was unreachable.
    /// </summary>
    public async Task<bool> UploadPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!await _uploading.WaitAsync(0, cancellationToken)) return true;
        try
        {
            foreach (var path in _spool.Pending())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var capture = _spool.Read(path);
                if (capture is null) continue;   // unreadable / not authentic: already removed
                var outcome = await _api.UploadCaptureAsync(_accessToken(), _sessionId, capture.Value.Info, capture.Value.Image, cancellationToken);
                if (outcome == DeliveryOutcome.Offline) return false;
                _spool.Remove(path);
            }
            return true;
        }
        finally
        {
            _uploading.Release();
        }
    }
}
