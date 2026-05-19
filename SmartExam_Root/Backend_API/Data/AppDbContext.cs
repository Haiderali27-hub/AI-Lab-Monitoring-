using Backend_API.Models;
using Backend_API.Models.Enums;
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

        // Enum conversions — store as strings in DB (readable, not magic numbers)
        modelBuilder.Entity<User>().Property(u => u.Role).HasConversion<string>();
        modelBuilder.Entity<Exam>().Property(e => e.Status).HasConversion<string>();
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
    }
}
