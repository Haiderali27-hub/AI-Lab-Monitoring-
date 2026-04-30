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
            entity
                .HasOne(x => x.Institution)
                .WithMany(x => x.Exams)
                .HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.Restrict);
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