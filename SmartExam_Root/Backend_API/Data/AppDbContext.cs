using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Models.Analytics;
using Backend_API.Models.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<DeviceBinding> DeviceBindings => Set<DeviceBinding>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Lab> Labs => Set<Lab>();
    public DbSet<Workstation> Workstations => Set<Workstation>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<SectionEnrollment> SectionEnrollments => Set<SectionEnrollment>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<ExamAssignment> ExamAssignments => Set<ExamAssignment>();
    public DbSet<ExamSession> ExamSessions => Set<ExamSession>();
    public DbSet<Answer> Answers => Set<Answer>();
    public DbSet<MonitoringEvent> MonitoringEvents => Set<MonitoringEvent>();
    public DbSet<AiGradingResult> AiGradingResults => Set<AiGradingResult>();
    public DbSet<TeacherGradeOverride> TeacherGradeOverrides => Set<TeacherGradeOverride>();
    public DbSet<PlagiarismResult> PlagiarismResults => Set<PlagiarismResult>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ExamReport> ExamReports => Set<ExamReport>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ReadinessCheck> ReadinessChecks => Set<ReadinessCheck>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ExamProctor> ExamProctors => Set<ExamProctor>();
    public DbSet<Screenshot> Screenshots => Set<Screenshot>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<TestCaseResult> TestCaseResults => Set<TestCaseResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure primary keys for entities with non-standard names
        modelBuilder.Entity<User>().HasKey(u => u.UserId);
        modelBuilder.Entity<DeviceBinding>().HasKey(d => d.BindingId);
        modelBuilder.Entity<UserSession>().HasKey(s => s.SessionId);
        modelBuilder.Entity<Lab>().HasKey(l => l.LabId);
        modelBuilder.Entity<Workstation>().HasKey(w => w.WorkstationId);
        modelBuilder.Entity<Department>().HasKey(d => d.DeptId);
        modelBuilder.Entity<Course>().HasKey(c => c.CourseId);
        modelBuilder.Entity<Section>().HasKey(s => s.SectionId);
        modelBuilder.Entity<SectionEnrollment>().HasKey(se => se.EnrollmentId);
        modelBuilder.Entity<Exam>().HasKey(e => e.ExamId);
        modelBuilder.Entity<Question>().HasKey(q => q.QuestionId);
        modelBuilder.Entity<TestCase>().HasKey(tc => tc.TestCaseId);
        modelBuilder.Entity<ExamAssignment>().HasKey(ea => ea.AssignmentId);
        modelBuilder.Entity<ExamSession>().HasKey(es => es.SessionId);
        modelBuilder.Entity<Answer>().HasKey(a => a.AnswerId);
        modelBuilder.Entity<MonitoringEvent>().HasKey(me => me.EventId);
        modelBuilder.Entity<AiGradingResult>().HasKey(agr => agr.ResultId);
        modelBuilder.Entity<TeacherGradeOverride>().HasKey(tgo => tgo.OverrideId);
        modelBuilder.Entity<PlagiarismResult>().HasKey(pr => pr.PlagId);
        modelBuilder.Entity<AuditLog>().HasKey(al => al.LogId);
        modelBuilder.Entity<ExamReport>().HasKey(r => r.ReportId);
        modelBuilder.Entity<Notification>().HasKey(n => n.NotificationId);

        // Enum conversions — store as strings in DB (readable, not magic numbers)
        modelBuilder.Entity<User>().Property(u => u.Role).HasConversion<string>();
        modelBuilder.Entity<Exam>().Property(e => e.Status).HasConversion<string>();
        modelBuilder.Entity<Exam>().Property(e => e.LockdownMode).HasConversion<string>().HasMaxLength(16);
        modelBuilder.Entity<ExamSession>().Property(s => s.Status).HasConversion<string>();
        modelBuilder.Entity<Question>().Property(q => q.Type).HasConversion<string>();
        modelBuilder.Entity<MonitoringEvent>().Property(m => m.EventType).HasConversion<string>();

        // Unique constraints
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<DeviceBinding>().HasIndex(d => d.UserId).IsUnique();
        modelBuilder.Entity<UserSession>().HasIndex(s => s.Jti).IsUnique();

        // DeviceBinding: one-to-one with User
        modelBuilder.Entity<DeviceBinding>()
            .HasOne(d => d.User)
            .WithOne(u => u.DeviceBinding)
            .HasForeignKey<DeviceBinding>(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Section: Teacher FK (no cascade to avoid cycle)
        modelBuilder.Entity<Section>()
            .HasOne(s => s.Teacher)
            .WithMany()
            .HasForeignKey(s => s.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        // PlagiarismResult: two student FKs (no cascade)
        modelBuilder.Entity<PlagiarismResult>()
            .HasOne(p => p.StudentA)
            .WithMany()
            .HasForeignKey(p => p.UserIdA)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PlagiarismResult>()
            .HasOne(p => p.StudentB)
            .WithMany()
            .HasForeignKey(p => p.UserIdB)
            .OnDelete(DeleteBehavior.Restrict);

        // TeacherGradeOverride: Teacher FK (no cascade)
        modelBuilder.Entity<TeacherGradeOverride>()
            .HasOne(t => t.Teacher)
            .WithMany()
            .HasForeignKey(t => t.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        // AuditLog: optional actor FK
        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.Actor)
            .WithMany()
            .HasForeignKey(a => a.ActorId)
            .OnDelete(DeleteBehavior.SetNull);

        // Answer → ExamSession: the scalar FK is named SessionId (not ExamSessionId),
        // so EF convention would otherwise create a stray shadow FK (ExamSessionSessionId)
        // and leave SessionId disconnected — which breaks answer inserts and the
        // answered-count metric. Bind the navigation explicitly to SessionId.
        modelBuilder.Entity<Answer>()
            .HasOne(a => a.ExamSession)
            .WithMany(s => s.Answers)
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Answer: one-to-one with AiGradingResult
        modelBuilder.Entity<AiGradingResult>()
            .HasOne(a => a.Answer)
            .WithOne(a => a.AiGradingResult)
            .HasForeignKey<AiGradingResult>(a => a.AnswerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Answer: one-to-one with TeacherGradeOverride
        modelBuilder.Entity<TeacherGradeOverride>()
            .HasOne(t => t.Answer)
            .WithOne(a => a.TeacherGradeOverride)
            .HasForeignKey<TeacherGradeOverride>(t => t.AnswerId)
            .OnDelete(DeleteBehavior.Cascade);

        // ExamReport: GeneratedByUser mapping
        modelBuilder.Entity<ExamReport>()
            .HasOne(r => r.GeneratedByUser)
            .WithMany()
            .HasForeignKey(r => r.GeneratedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Notification: Recipient mapping
        modelBuilder.Entity<Notification>()
            .HasOne(n => n.Recipient)
            .WithMany()
            .HasForeignKey(n => n.RecipientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for fast unread notification queries
        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.RecipientId, n.IsRead });

        // Module 2 readiness checks — latest per (exam, student) is what gates start
        modelBuilder.Entity<ReadinessCheck>().HasKey(r => r.Id);
        modelBuilder.Entity<ReadinessCheck>().Property(r => r.MachineName).HasMaxLength(128);
        modelBuilder.Entity<ReadinessCheck>()
            .HasIndex(r => new { r.ExamId, r.UserId, r.CheckedAt });
        modelBuilder.Entity<ReadinessCheck>()
            .HasOne(r => r.Exam).WithMany().HasForeignKey(r => r.ExamId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ReadinessCheck>()
            .HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);

        // Module 9 attendance — one row per (section, student), upserted by the CSV import
        modelBuilder.Entity<AttendanceRecord>().HasKey(r => r.AttendanceId);
        modelBuilder.Entity<AttendanceRecord>()
            .HasIndex(r => new { r.SectionId, r.UserId }).IsUnique();
        modelBuilder.Entity<AttendanceRecord>()
            .HasOne(r => r.Section).WithMany().HasForeignKey(r => r.SectionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AttendanceRecord>()
            .HasOne(r => r.Student).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);

        // Module 9 eligibility provenance + seat identity
        modelBuilder.Entity<ExamAssignment>().Property(a => a.EligibilitySource).HasConversion<string>().HasMaxLength(16);
        modelBuilder.Entity<Workstation>().Property(w => w.HostName).HasMaxLength(128);

        // Module 1: unique roll number (NULL for staff and for students without one)
        modelBuilder.Entity<User>().Property(u => u.RollNumber).HasMaxLength(32);
        modelBuilder.Entity<User>().HasIndex(u => u.RollNumber).IsUnique().HasFilter("\"RollNumber\" IS NOT NULL");

        // Module 1: rotating refresh tokens (hash only). RevokedAt is a concurrency token so two
        // concurrent refreshes with the same token cannot both rotate it.
        modelBuilder.Entity<RefreshToken>().HasKey(t => t.TokenId);
        modelBuilder.Entity<RefreshToken>().Property(t => t.TokenHash).HasMaxLength(64);
        modelBuilder.Entity<RefreshToken>().Property(t => t.Jti).HasMaxLength(64);
        modelBuilder.Entity<RefreshToken>().Property(t => t.Channel).HasMaxLength(32);
        modelBuilder.Entity<RefreshToken>().Property(t => t.MachineName).HasMaxLength(128);
        modelBuilder.Entity<RefreshToken>().Property(t => t.RevokedReason).HasMaxLength(32);
        modelBuilder.Entity<RefreshToken>().Property(t => t.CreatedByIp).HasMaxLength(64);
        modelBuilder.Entity<RefreshToken>().Property(t => t.RevokedAt).IsConcurrencyToken();
        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.TokenHash).IsUnique();
        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.FamilyId);
        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.Jti);
        modelBuilder.Entity<RefreshToken>().HasIndex(t => new { t.UserId, t.RevokedAt });
        modelBuilder.Entity<RefreshToken>()
            .HasOne(t => t.User).WithMany(u => u.RefreshTokens).HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);

        // Module 7: academic structure. Names/codes are unique (the API also checks, case-insensitively,
        // because the InMemory provider used by the tests does not enforce unique indexes).
        modelBuilder.Entity<Department>().HasIndex(d => d.Name).IsUnique();
        modelBuilder.Entity<Course>().HasIndex(c => new { c.DeptId, c.Code }).IsUnique();
        // Course → Department: the scalar FK is DeptId. Without this, EF convention bound the navigation to a
        // shadow column (DepartmentDeptId) and left DeptId disconnected (it stayed all-zero for seeded rows) —
        // the same class of bug as Answer.SessionId. Restrict: a department with courses is never deleted.
        modelBuilder.Entity<Course>()
            .HasOne(c => c.Department)
            .WithMany(d => d.Courses)
            .HasForeignKey(c => c.DeptId)
            .OnDelete(DeleteBehavior.Restrict);

        // Module 7/10: proctor assignments — who may supervise which exam (invigilator scoping).
        modelBuilder.Entity<ExamProctor>().HasKey(p => new { p.ExamId, p.UserId });
        modelBuilder.Entity<ExamProctor>().HasIndex(p => p.UserId);
        modelBuilder.Entity<ExamProctor>()
            .HasOne(p => p.Exam).WithMany().HasForeignKey(p => p.ExamId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ExamProctor>()
            .HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

        // Module 6: one answer row per (session, question) — the upsert in save-answer relies on it
        // (it also checks in code: the InMemory provider used by the tests does not enforce unique indexes).
        // Revision is a concurrency token so two racing revisioned saves cannot both win; Status on the
        // session is one so a student submit and the timeout sweeper (or a proctor) cannot both finalise it.
        modelBuilder.Entity<Answer>().HasIndex(a => new { a.SessionId, a.QuestionId }).IsUnique();
        modelBuilder.Entity<Answer>().Property(a => a.Revision).IsConcurrencyToken();
        modelBuilder.Entity<ExamSession>().Property(s => s.Status).IsConcurrencyToken();
        modelBuilder.Entity<ExamSession>().Property(s => s.ReceiptSha256).HasMaxLength(64);

        // Module 5: offline replay dedup — one row per (session, client event id). Partial, so the many
        // server-generated events (ClientEventId NULL) are not constrained. The batch endpoint also dedups in
        // code, because the InMemory provider used by the tests does not enforce unique indexes.
        modelBuilder.Entity<MonitoringEvent>()
            .HasIndex(e => new { e.ExamSessionId, e.ClientEventId })
            .IsUnique()
            .HasFilter("\"ClientEventId\" IS NOT NULL");
        // Timeline and violation-count queries read one session's events in time order.
        modelBuilder.Entity<MonitoringEvent>().HasIndex(e => new { e.ExamSessionId, e.RecordedAt });
        // The heartbeat watchdog scans running sessions by their last heartbeat.
        modelBuilder.Entity<ExamSession>().HasIndex(s => new { s.Status, s.LastHeartbeatAt });

        // Module 5: capture evidence (screen / webcam). Rows go with their session; the image files are owned by
        // EvidenceStorage and removed by the retention job.
        modelBuilder.Entity<Screenshot>().HasKey(s => s.ScreenshotId);
        modelBuilder.Entity<Screenshot>().Property(s => s.Kind).HasMaxLength(16);
        modelBuilder.Entity<Screenshot>().Property(s => s.Trigger).HasMaxLength(16);
        modelBuilder.Entity<Screenshot>().Property(s => s.TriggerReason).HasMaxLength(64);
        modelBuilder.Entity<Screenshot>().Property(s => s.StoragePath).HasMaxLength(512);
        modelBuilder.Entity<Screenshot>().Property(s => s.ContentType).HasMaxLength(32);
        modelBuilder.Entity<Screenshot>().Property(s => s.Sha256).HasMaxLength(64);
        modelBuilder.Entity<Screenshot>().HasIndex(s => new { s.SessionId, s.CapturedAt });
        modelBuilder.Entity<Screenshot>().HasIndex(s => s.ExamId);
        modelBuilder.Entity<Screenshot>().HasIndex(s => s.CapturedAt);
        modelBuilder.Entity<Screenshot>()
            .HasOne(s => s.Session).WithMany().HasForeignKey(s => s.SessionId).OnDelete(DeleteBehavior.Cascade);

        // Module 10: proctor incidents. Rows go with their session; the drawer and the tile badges list them per exam.
        modelBuilder.Entity<Incident>().HasKey(i => i.IncidentId);
        modelBuilder.Entity<Incident>().Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
        modelBuilder.Entity<Incident>().Property(i => i.Severity).HasConversion<string>().HasMaxLength(16);
        modelBuilder.Entity<Incident>().Property(i => i.Notes).HasMaxLength(Incident.MaxNotesLength);
        modelBuilder.Entity<Incident>().Property(i => i.StatusNote).HasMaxLength(Incident.MaxNotesLength);
        modelBuilder.Entity<Incident>().HasIndex(i => new { i.ExamId, i.Status });
        modelBuilder.Entity<Incident>().HasIndex(i => i.SessionId);
        modelBuilder.Entity<Incident>()
            .HasOne(i => i.Session).WithMany().HasForeignKey(i => i.SessionId).OnDelete(DeleteBehavior.Cascade);

        // Module 11: AI-assisted evaluation. Status drives the background grading queue (Pending rows are re-queued on
        // start); QueueVersion is a concurrency token so a run that finishes after a regrade was requested cannot
        // overwrite the newer job. Per-test-case results go with their grading row.
        modelBuilder.Entity<AiGradingResult>().Property(g => g.Status).HasMaxLength(16);
        modelBuilder.Entity<AiGradingResult>().Property(g => g.Engine).HasMaxLength(64);
        modelBuilder.Entity<AiGradingResult>().Property(g => g.CompileOutput).HasMaxLength(AiGradingResult.MaxCompileOutputLength + 8);
        modelBuilder.Entity<AiGradingResult>().Property(g => g.QueueVersion).IsConcurrencyToken();
        modelBuilder.Entity<AiGradingResult>().HasIndex(g => g.Status);
        modelBuilder.Entity<TestCaseResult>().HasKey(r => r.TestCaseResultId);
        modelBuilder.Entity<TestCaseResult>().Property(r => r.Name).HasMaxLength(64);
        modelBuilder.Entity<TestCaseResult>().Property(r => r.Status).HasMaxLength(16);
        modelBuilder.Entity<TestCaseResult>().Property(r => r.Category).HasMaxLength(TestCase.MaxCategoryLength);
        modelBuilder.Entity<TestCaseResult>().Property(r => r.CompareMode).HasMaxLength(32);
        modelBuilder.Entity<TestCaseResult>().Property(r => r.ActualOutput).HasMaxLength(TestCaseResult.MaxOutputLength + 8);
        modelBuilder.Entity<TestCaseResult>().HasIndex(r => new { r.ResultId, r.OrderIndex });
        modelBuilder.Entity<TestCaseResult>()
            .HasOne(r => r.AiGradingResult).WithMany(g => g.TestCaseResults).HasForeignKey(r => r.ResultId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TestCase>().Property(tc => tc.Category).HasMaxLength(TestCase.MaxCategoryLength);
        modelBuilder.Entity<Question>().Property(q => q.Language).HasMaxLength(16);
        modelBuilder.Entity<Question>().Property(q => q.RubricKeywords).HasMaxLength(1000);
    }
}
