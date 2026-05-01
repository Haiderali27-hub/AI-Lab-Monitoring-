using Backend_API.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<User> Users => Set<User>();
    public DbSet<DeviceBinding> DeviceBindings => Set<DeviceBinding>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamAssignment> ExamAssignments => Set<ExamAssignment>();
    public DbSet<ExamSession> ExamSessions => Set<ExamSession>();
    public DbSet<MonitoringEvent> MonitoringEvents => Set<MonitoringEvent>();
    public DbSet<Lab> Labs => Set<Lab>();
    public DbSet<Workstation> Workstations => Set<Workstation>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<SectionEnrollment> SectionEnrollments => Set<SectionEnrollment>();
    public DbSet<TeacherSectionAssignment> TeacherSectionAssignments => Set<TeacherSectionAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Institution>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ContactEmail).HasMaxLength(256).IsRequired();
            entity.Property(x => x.LogoUrl).HasMaxLength(1024);
            entity.Property(x => x.AllowedIpRanges).HasMaxLength(2000);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Lab>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity
                .HasOne(x => x.Institution)
                .WithMany(x => x.Labs)
                .HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(40);
            entity
                .HasOne(x => x.Institution)
                .WithMany(x => x.Departments)
                .HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Course>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(40);
            entity
                .HasOne(x => x.Institution)
                .WithMany(x => x.Courses)
                .HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.NoAction);
            entity
                .HasOne(x => x.Department)
                .WithMany(x => x.Courses)
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Section>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(60);
            entity
                .HasOne(x => x.Institution)
                .WithMany(x => x.Sections)
                .HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.NoAction);
            entity
                .HasOne(x => x.Department)
                .WithMany(x => x.Sections)
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.NoAction);
            entity
                .HasOne(x => x.Course)
                .WithMany(x => x.Sections)
                .HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Workstation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(50);
            entity.HasIndex(x => new { x.LabId, x.Name }).IsUnique();
            entity
                .HasOne(x => x.Lab)
                .WithMany(x => x.Workstations)
                .HasForeignKey(x => x.LabId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(64);
            entity.HasIndex(x => new { x.InstitutionId, x.Username }).IsUnique();
            entity.HasIndex(x => new { x.InstitutionId, x.Email }).IsUnique();
            entity
                .HasOne(x => x.Institution)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SectionEnrollment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity
                .HasOne(x => x.Section)
                .WithMany(x => x.Enrollments)
                .HasForeignKey(x => x.SectionId)
                .OnDelete(DeleteBehavior.NoAction);
            entity
                .HasOne(x => x.StudentUser)
                .WithMany(x => x.SectionEnrollments)
                .HasForeignKey(x => x.StudentUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TeacherSectionAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity
                .HasOne(x => x.Section)
                .WithMany(x => x.TeacherAssignments)
                .HasForeignKey(x => x.SectionId)
                .OnDelete(DeleteBehavior.NoAction);
            entity
                .HasOne(x => x.TeacherUser)
                .WithMany(x => x.TeacherSectionAssignments)
                .HasForeignKey(x => x.TeacherUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<DeviceBinding>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.HwidHash).HasMaxLength(256).IsRequired();
            entity.HasIndex(x => x.StudentUserId).IsUnique();
            entity
                .HasOne(x => x.StudentUser)
                .WithMany(x => x.DeviceBindings)
                .HasForeignKey(x => x.StudentUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AccessTokenJti).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RefreshTokenHash).HasMaxLength(256).IsRequired();
            entity.HasIndex(x => x.RefreshTokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
            entity
                .HasOne(x => x.User)
                .WithMany(x => x.Sessions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Exam>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Instructions).HasMaxLength(4000);
            entity
                .HasOne(x => x.Institution)
                .WithMany(x => x.Exams)
                .HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity
                .HasOne(x => x.Lab)
                .WithMany(x => x.Exams)
                .HasForeignKey(x => x.LabId)
                .OnDelete(DeleteBehavior.SetNull);
            entity
                .HasOne(x => x.ProctorUser)
                .WithMany(x => x.ProctoredExams)
                .HasForeignKey(x => x.ProctorUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ExamAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ExamId, x.StudentUserId }).IsUnique();
            entity
                .HasOne(x => x.Exam)
                .WithMany(x => x.Assignments)
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne(x => x.StudentUser)
                .WithMany(x => x.ExamAssignments)
                .HasForeignKey(x => x.StudentUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne(x => x.Workstation)
                .WithMany(x => x.ExamAssignments)
                .HasForeignKey(x => x.WorkstationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ExamSession>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
            entity.HasIndex(x => new { x.ExamId, x.StudentUserId, x.Status });
            entity
                .HasOne(x => x.Exam)
                .WithMany(x => x.Sessions)
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne(x => x.StudentUser)
                .WithMany(x => x.ExamSessions)
                .HasForeignKey(x => x.StudentUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MonitoringEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("text");
            entity.HasIndex(x => new { x.StudentUserId, x.CreatedAtUtc });
            entity
                .HasOne(x => x.StudentUser)
                .WithMany(x => x.MonitoringEvents)
                .HasForeignKey(x => x.StudentUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne(x => x.ExamSession)
                .WithMany(x => x.MonitoringEvents)
                .HasForeignKey(x => x.ExamSessionId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
