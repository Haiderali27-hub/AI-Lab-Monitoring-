using Backend_API.Data;
using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Tests.Tests.Users;

/// <summary>
/// M7-U-01…02 — the first SuperAdmin (no API can create one) comes from configuration, and the demo
/// seed contains a SuperAdmin plus an invigilator assigned to the open demo exam.
/// </summary>
public class SuperAdminBootstrapTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"bootstrap_{Guid.NewGuid():N}").Options);

    private static User NewUser(string email, UserRole role)
    {
        var salt = PasswordHelper.GenerateSalt();
        return new User { Name = email, Email = email, Role = role, Salt = salt, PasswordHash = PasswordHelper.HashPassword("Old@12345", salt) };
    }

    [Fact]
    public async Task M7_U_01_Bootstrap_CreatesPromotesOrLeavesAlone()
    {
        using (var db = NewDb())
        {
            (await SuperAdminBootstrap.EnsureAsync(db, null, null)).Should().Be(SuperAdminBootstrap.Outcome.NotConfigured);
            (await SuperAdminBootstrap.EnsureAsync(db, "root@uni.edu", "short")).Should().Be(SuperAdminBootstrap.Outcome.InvalidPassword);
            (await db.Users.AnyAsync()).Should().BeFalse();

            (await SuperAdminBootstrap.EnsureAsync(db, " root@uni.edu ", "Str0ng!Pass")).Should().Be(SuperAdminBootstrap.Outcome.Created);
            var root = await db.Users.SingleAsync();
            root.Email.Should().Be("root@uni.edu");
            root.Role.Should().Be(UserRole.SuperAdmin);
            PasswordHelper.VerifyPassword("Str0ng!Pass", root.Salt, root.PasswordHash).Should().BeTrue();
            (await db.AuditLogs.SingleAsync()).EventType.Should().Be("SUPERADMIN_BOOTSTRAPPED");

            // Once a SuperAdmin exists the bootstrap is a no-op, whatever is configured.
            (await SuperAdminBootstrap.EnsureAsync(db, "other@uni.edu", "Str0ng!Pass")).Should().Be(SuperAdminBootstrap.Outcome.AlreadyPresent);
            (await db.Users.CountAsync()).Should().Be(1);
        }

        using (var db = NewDb())
        {
            var admin = NewUser("head@uni.edu", UserRole.Admin);
            admin.IsActive = false;
            db.Users.Add(admin);
            await db.SaveChangesAsync();

            (await SuperAdminBootstrap.EnsureAsync(db, "head@uni.edu", null)).Should().Be(SuperAdminBootstrap.Outcome.Promoted);
            var promoted = await db.Users.SingleAsync();
            promoted.Role.Should().Be(UserRole.SuperAdmin);
            promoted.IsActive.Should().BeTrue();
            PasswordHelper.VerifyPassword("Old@12345", promoted.Salt, promoted.PasswordHash).Should().BeTrue("promotion keeps the password");
        }
    }

    [Fact]
    public async Task M7_U_02_DemoSeed_HasSuperAdmin_AndInvigilatorProctoringTheOpenExam()
    {
        using var db = NewDb();
        await DbSeeder.SeedAsync(db);

        (await db.Users.CountAsync(u => u.Role == UserRole.SuperAdmin)).Should().Be(1);
        var invigilator = await db.Users.SingleAsync(u => u.Role == UserRole.Invigilator);
        var proctored = await db.ExamProctors.Where(p => p.UserId == invigilator.UserId).Select(p => p.Exam.Title).ToListAsync();
        proctored.Should().ContainSingle().Which.Should().Be("Mid-Term Lab Exam (Demo)");

        // Seeded courses are linked to their department through DeptId (it used to stay all-zero).
        var course = await db.Courses.SingleAsync();
        course.DeptId.Should().Be((await db.Departments.SingleAsync()).DeptId);
    }
}
