using Backend_API.Contracts;
using Backend_API.Data;
using Backend_API.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services;

public class SeedService(AppDbContext dbContext, IPasswordService passwordService) : ISeedService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IPasswordService _passwordService = passwordService;

    public async Task EnsureSuperAdminAsync(CancellationToken cancellationToken)
    {
        if (await _dbContext.Users.AnyAsync(x => x.Role == SystemRole.SuperAdmin, cancellationToken))
        {
            return;
        }

        _dbContext.Users.Add(new User
        {
            Username = "superadmin",
            Email = "super@smartexam.local",
            PasswordHash = _passwordService.HashPassword("SuperAdmin@123"),
            Role = SystemRole.SuperAdmin,
            IsActive = true
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ServiceResult<SeedDemoResponse>> SeedDemoAsync(
        Guid institutionId,
        SeedDemoRequest request,
        CancellationToken cancellationToken)
    {
        var institutionExists = await _dbContext.Institutions.AnyAsync(x => x.Id == institutionId, cancellationToken);
        if (!institutionExists)
        {
            return ServiceResult<SeedDemoResponse>.Fail("INSTITUTION_NOT_FOUND", "Institution not found.");
        }

        var teachersCreated = 0;
        var studentsCreated = 0;

        for (var index = 1; index <= request.TeacherCount; index++)
        {
            var username = $"teacher{index}";
            if (await _dbContext.Users.AnyAsync(x => x.InstitutionId == institutionId && x.Username == username, cancellationToken))
            {
                continue;
            }

            _dbContext.Users.Add(new User
            {
                InstitutionId = institutionId,
                Username = username,
                Email = $"{username}@smartexam.local",
                PasswordHash = _passwordService.HashPassword("Teacher@123"),
                Role = SystemRole.Teacher,
                IsActive = true
            });
            teachersCreated++;
        }

        for (var index = 1; index <= request.StudentCount; index++)
        {
            var username = $"student{index}";
            if (await _dbContext.Users.AnyAsync(x => x.InstitutionId == institutionId && x.Username == username, cancellationToken))
            {
                continue;
            }

            _dbContext.Users.Add(new User
            {
                InstitutionId = institutionId,
                Username = username,
                Email = $"{username}@smartexam.local",
                PasswordHash = _passwordService.HashPassword("Student@123"),
                Role = SystemRole.Student,
                IsActive = true
            });
            studentsCreated++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var examsCreated = 0;
        var assignmentsCreated = 0;
        var students = await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId && x.Role == SystemRole.Student)
            .ToListAsync(cancellationToken);

        for (var index = 1; index <= request.ExamCount; index++)
        {
            var examName = $"Demo Exam {index}";
            if (await _dbContext.Exams.AnyAsync(x => x.InstitutionId == institutionId && x.Name == examName, cancellationToken))
            {
                continue;
            }

            var exam = new Exam
            {
                InstitutionId = institutionId,
                Name = examName,
                StartUtc = DateTime.UtcNow.AddMinutes(-10),
                EndUtc = DateTime.UtcNow.AddHours(2),
                IsActive = true
            };

            _dbContext.Exams.Add(exam);
            examsCreated++;

            foreach (var student in students)
            {
                if (await _dbContext.ExamAssignments.AnyAsync(
                        x => x.ExamId == exam.Id && x.StudentUserId == student.Id,
                        cancellationToken))
                {
                    continue;
                }

                _dbContext.ExamAssignments.Add(new ExamAssignment
                {
                    ExamId = exam.Id,
                    StudentUserId = student.Id,
                    IsEligible = true
                });
                assignmentsCreated++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<SeedDemoResponse>.Ok(new SeedDemoResponse(
            teachersCreated,
            studentsCreated,
            examsCreated,
            assignmentsCreated),
            "Demo data created.");
    }
}