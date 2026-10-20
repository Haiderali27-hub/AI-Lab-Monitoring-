using Microsoft.AspNetCore.Http;

namespace Backend_API.DTOs;

/// <summary>
/// Module 5: multipart body of <c>POST /api/monitoring/screenshot</c>. Only <see cref="ExamSessionId"/> and
/// <see cref="File"/> are required; the server decides the stored name, the content type (from the file's first
/// bytes) and clamps an absurd capture time.
/// </summary>
public sealed class ScreenshotUploadForm
{
    public Guid ExamSessionId { get; set; }
    /// <summary>Screen (default) or Webcam.</summary>
    public string? Kind { get; set; }
    /// <summary>Client capture time, ISO 8601 (UTC). Missing or absurd → server time.</summary>
    public string? CapturedAt { get; set; }
    /// <summary>Interval (default), Violation or Manual.</summary>
    public string? Trigger { get; set; }
    /// <summary>For a violation capture: the violation that triggered it (e.g. UnauthorizedProcess).</summary>
    public string? Reason { get; set; }
    public IFormFile? File { get; set; }
}
