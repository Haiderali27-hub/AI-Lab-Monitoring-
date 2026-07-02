using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Backend_API.Data;
using Microsoft.EntityFrameworkCore;
using Backend_API.Models.Enums;

namespace Backend_API.Hubs;

[Authorize]
public class MonitoringHub(AppDbContext db) : Hub
{
    private readonly AppDbContext _db = db;

    public async Task JoinInstitution(Guid institutionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"institution:{institutionId}");
    }

    public async Task LeaveInstitution(Guid institutionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"institution:{institutionId}");
    }

    public async Task SendWarning(string studentId, string message)
    {
        if (Guid.TryParse(studentId, out var studentGuid))
        {
            var user = await _db.Users.FindAsync(studentGuid);
            if (user is not null)
            {
                _db.Notifications.Add(new Models.Notifications.Notification
                {
                    NotificationId = Guid.NewGuid(),
                    RecipientId = studentGuid,
                    Title = "Proctor Warning",
                    Body = message,
                    Type = "Warning",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
        }
    }

    public async Task ForceSubmitSession(string studentId)
    {
        if (Guid.TryParse(studentId, out var studentGuid))
        {
            var session = await _db.ExamSessions
                .Where(s => s.UserId == studentGuid && s.Status == SessionStatus.InProgress)
                .FirstOrDefaultAsync();

            if (session is not null)
            {
                session.Status = SessionStatus.ForceSubmitted;
                session.SubmittedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
    }
}
