using Backend_API.Data;
using Backend_API.Models.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services.Notifications;

public class NotificationService
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;

    public NotificationService(AppDbContext db, EmailService email)
    {
        _db = db;
        _email = email;
    }

    // ── Core method — creates notification + sends email ──────────────────────

    public async Task SendAsync(
        Guid recipientId,
        string title,
        string body,
        string type,
        string? relatedEntityId = null,
        bool sendEmail = true)
    {
        // Save in-app notification
        var notification = new Notification
        {
            RecipientId = recipientId,
            Title = title,
            Body = body,
            Type = type,
            RelatedEntityId = relatedEntityId
        };
        _db.Notifications.Add(notification);

        // Send email if requested
        if (sendEmail)
        {
            var recipient = await _db.Users.FindAsync(recipientId);
            if (recipient is not null && !string.IsNullOrEmpty(recipient.Email))
            {
                var html = BuildEmailHtml(title, body, type);
                await _email.SendAsync(recipient.Email, recipient.Name, $"SmartExam — {title}", html);
                notification.EmailSent = true;
            }
        }

        await _db.SaveChangesAsync();
    }

    // ── Bulk send to many recipients ──────────────────────────────────────────

    public async Task SendToManyAsync(
        IEnumerable<Guid> recipientIds,
        string title,
        string body,
        string type,
        string? relatedEntityId = null,
        bool sendEmail = true)
    {
        foreach (var id in recipientIds)
            await SendAsync(id, title, body, type, relatedEntityId, sendEmail);
    }

    // ── Pre-built notification templates ─────────────────────────────────────

    public async Task NotifyExamScheduledAsync(Guid examId)
    {
        var exam = await _db.Exams
            .Include(e => e.Assignments).ThenInclude(a => a.Student)
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .FirstOrDefaultAsync(e => e.ExamId == examId);
        if (exam is null) return;

        var studentIds = exam.Assignments
            .Where(a => a.IsEligible)
            .Select(a => a.UserId);

        await SendToManyAsync(
            studentIds,
            "Exam Scheduled",
            $"Your exam '{exam.Title}' for {exam.Section.Course.Name} has been scheduled for {exam.StartTime:dddd, dd MMM yyyy} at {exam.StartTime:hh:mm tt}. Duration: {exam.DurationMinutes} minutes.",
            "ExamScheduled",
            examId.ToString()
        );
    }

    public async Task NotifyGradesReleasedAsync(Guid examId)
    {
        var exam = await _db.Exams
            .Include(e => e.Sessions)
            .FirstOrDefaultAsync(e => e.ExamId == examId);
        if (exam is null) return;

        var studentIds = exam.Sessions.Select(s => s.UserId);

        await SendToManyAsync(
            studentIds,
            "Grades Released",
            $"Your grades for '{exam.Title}' are now available. Log in to your SmartExam student portal to view your results and AI feedback.",
            "GradeReleased",
            examId.ToString()
        );
    }

    public async Task NotifyViolationAsync(Guid studentId, string examTitle, string violationType)
    {
        await SendAsync(
            studentId,
            "Exam Violation Recorded",
            $"A violation was recorded during your exam '{examTitle}'. Violation type: {violationType}. This has been reported to your proctor.",
            "ViolationWarning",
            sendEmail: false // violations are in-app only, not email
        );
    }

    public async Task NotifyEligibilityChangedAsync(Guid studentId, string examTitle, bool isNowEligible, string? reason)
    {
        string status = isNowEligible ? "eligible" : "ineligible";
        string reasonText = reason is not null ? $" Reason: {reason}" : string.Empty;

        await SendAsync(
            studentId,
            "Eligibility Updated",
            $"Your eligibility for '{examTitle}' has been updated. You are now {status}.{reasonText}",
            "EligibilityChanged"
        );
    }

    public async Task NotifyExamReminderAsync(Guid examId)
    {
        var exam = await _db.Exams
            .Include(e => e.Assignments)
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .FirstOrDefaultAsync(e => e.ExamId == examId);
        if (exam is null) return;

        var studentIds = exam.Assignments
            .Where(a => a.IsEligible)
            .Select(a => a.UserId);

        await SendToManyAsync(
            studentIds,
            "Exam Reminder — Tomorrow",
            $"Reminder: Your exam '{exam.Title}' for {exam.Section.Course.Name} is tomorrow at {exam.StartTime:hh:mm tt}. Make sure to be at your assigned workstation on time.",
            "ExamReminder",
            examId.ToString()
        );
    }

    // ── Email HTML template ───────────────────────────────────────────────────

    private static string BuildEmailHtml(string title, string body, string type)
    {
        string iconColor = type switch
        {
            "ViolationWarning" => "#DC2626",
            "ExamScheduled"    => "#2563EB",
            "GradeReleased"    => "#16A34A",
            "EligibilityChanged" => "#D97706",
            _                  => "#2563EB"
        };

        return $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="font-family: Arial, sans-serif; background: #F8FAFC; margin: 0; padding: 0;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background: #F8FAFC; padding: 40px 0;">
            <tr><td align="center">
              <table width="560" cellpadding="0" cellspacing="0" style="background: #FFFFFF; border-radius: 8px; overflow: hidden; border: 1px solid #E2E8F0;">
                <!-- Header -->
                <tr><td style="background: {iconColor}; padding: 24px 32px;">
                  <p style="margin:0; color: #FFFFFF; font-size: 20px; font-weight: bold;">SmartExam</p>
                  <p style="margin:4px 0 0 0; color: rgba(255,255,255,0.8); font-size: 13px;">AI-Driven Lab Exam Monitoring System</p>
                </td></tr>
                <!-- Body -->
                <tr><td style="padding: 32px;">
                  <h2 style="margin: 0 0 16px 0; color: #0F172A; font-size: 18px;">{title}</h2>
                  <p style="margin: 0; color: #475569; font-size: 15px; line-height: 1.6;">{body}</p>
                </td></tr>
                <!-- Footer -->
                <tr><td style="background: #F8FAFC; padding: 16px 32px; border-top: 1px solid #E2E8F0;">
                  <p style="margin: 0; color: #94A3B8; font-size: 12px;">This is an automated message from SmartExam. Do not reply to this email.</p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
    }
}
