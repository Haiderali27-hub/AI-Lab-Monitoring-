using Backend_API.Contracts;
using Backend_API.Data;
using Backend_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend_API.Services;

public class AuthService(
    AppDbContext dbContext,
    IPasswordService passwordService,
    IJwtTokenService jwtTokenService,
    IHwidService hwidService,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IPasswordService _passwordService = passwordService;
    private readonly IJwtTokenService _jwtTokenService = jwtTokenService;
    private readonly IHwidService _hwidService = hwidService;
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;
    private readonly ILogger<AuthService> _logger = logger;

    public async Task<ServiceResult<TokenResponse>> BootstrapSuperAdminAsync(
        BootstrapAdminRequest request,
        CancellationToken cancellationToken)
    {
        try 
        {
            if (await _dbContext.Users.AnyAsync(x => x.Role == SystemRole.SuperAdmin, cancellationToken))
            {
                return ServiceResult<TokenResponse>.Fail("BOOTSTRAP_ALREADY_COMPLETED", "Platform is already initialized.");
            }

            var institution = await _dbContext.Institutions
                .FirstOrDefaultAsync(x => x.Name == request.InstitutionName.Trim(), cancellationToken);

            if (institution == null)
            {
                institution = new Institution
                {
                    Name = request.InstitutionName.Trim(),
                    ContactEmail = request.Email.Trim().ToLowerInvariant()
                };
                _dbContext.Institutions.Add(institution);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            var admin = new User
            {
                InstitutionId = institution.Id,
                Username = request.Username.Trim(),
                Email = request.Email.Trim().ToLowerInvariant(),
                PasswordHash = _passwordService.HashPassword(request.Password),
                Role = SystemRole.SuperAdmin,
                IsActive = true
            };

            _dbContext.Users.Add(admin);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return await IssueTokensAsync(admin, false, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOOTSTRAP ERROR] {ex.Message}");
            return ServiceResult<TokenResponse>.Fail("DATABASE_ERROR", $"Setup failed: {ex.Message}");
        }
    }

    public async Task<ServiceResult<TokenResponse>> BootstrapAdminAsync(
        BootstrapAdminRequest request,
        CancellationToken cancellationToken)
    {
        var existingInstitution = await _dbContext.Institutions.AnyAsync(cancellationToken);
        if (existingInstitution)
        {
            return ServiceResult<TokenResponse>.Fail(
                "BOOTSTRAP_ALREADY_COMPLETED",
                "Institution bootstrap is already completed.");
        }

        var institution = new Institution
        {
            Name = request.InstitutionName.Trim(),
            ContactEmail = request.Email.Trim().ToLowerInvariant()
        };

        var admin = new User
        {
            Institution = institution,
            Username = request.Username.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = _passwordService.HashPassword(request.Password),
            Role = SystemRole.OrganizationAdmin,
            IsActive = true
        };

        _dbContext.Institutions.Add(institution);
        _dbContext.Users.Add(admin);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await IssueTokensAsync(admin, false, cancellationToken);
    }

    public async Task<ServiceResult<TokenResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await FindUserByUsernameOrEmailAsync(request.UsernameOrEmail, cancellationToken);
        if (user is null || user.Role == SystemRole.Student || !user.IsActive)
        {
            return ServiceResult<TokenResponse>.Fail("INVALID_CREDENTIALS", "Invalid credentials.");
        }

        if (!_passwordService.VerifyPassword(request.Password, user.PasswordHash))
        {
            return ServiceResult<TokenResponse>.Fail("INVALID_CREDENTIALS", "Invalid credentials.");
        }

        return await IssueTokensAsync(user, false, cancellationToken);
    }

    public async Task<bool> IsBootstrappedAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Users.AnyAsync(
            x => x.Role == SystemRole.SuperAdmin || x.Role == SystemRole.OrganizationAdmin,
            cancellationToken);
    }

    public async Task<ServiceResult<TokenResponse>> StudentLoginAsync(
        StudentLoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await FindUserByUsernameOrEmailAsync(request.UsernameOrEmail, cancellationToken);
        if (user is null || user.Role != SystemRole.Student || !user.IsActive)
        {
            return ServiceResult<TokenResponse>.Fail("INVALID_CREDENTIALS", "Invalid credentials.");
        }

        if (!_passwordService.VerifyPassword(request.Password, user.PasswordHash))
        {
            return ServiceResult<TokenResponse>.Fail("INVALID_CREDENTIALS", "Invalid credentials.");
        }

        var activeSessionExists = await _dbContext.UserSessions.AnyAsync(
            x => x.UserId == user.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow,
            cancellationToken);

        if (activeSessionExists)
        {
            _logger.LogWarning("Blocked concurrent student login for user {UserId}", user.Id);
            return ServiceResult<TokenResponse>.Fail(
                "ACTIVE_SESSION_EXISTS",
                "An active session already exists. Contact admin to terminate the current session.");
        }

        var hardwareFingerprintHash = _hwidService.HashFingerprint(request.HardwareFingerprint);
        var binding = await _dbContext.DeviceBindings
            .FirstOrDefaultAsync(x => x.StudentUserId == user.Id, cancellationToken);

        if (binding is null)
        {
            binding = new DeviceBinding
            {
                StudentUserId = user.Id,
                HwidHash = hardwareFingerprintHash,
                BoundAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow
            };

            _dbContext.DeviceBindings.Add(binding);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (!string.Equals(binding.HwidHash, hardwareFingerprintHash, StringComparison.Ordinal))
        {
            _logger.LogWarning("Device mismatch for student {UserId}", user.Id);
            return ServiceResult<TokenResponse>.Fail(
                "DEVICE_MISMATCH",
                "This account is bound to another device. Ask admin to reset device binding.");
        }
        else
        {
            binding.LastSeenAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await IssueTokensAsync(user, true, cancellationToken);
    }

    public async Task<ServiceResult<TokenResponse>> RefreshAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return ServiceResult<TokenResponse>.Fail("INVALID_REFRESH_TOKEN", "Refresh token is required.");
        }

        var refreshHash = _jwtTokenService.HashToken(request.RefreshToken);
        var session = await _dbContext.UserSessions
            .Include(x => x.User)
            .FirstOrDefaultAsync(
                x => x.RefreshTokenHash == refreshHash &&
                     x.RevokedAtUtc == null &&
                     x.ExpiresAtUtc > DateTime.UtcNow,
                cancellationToken);

        if (session is null || !session.User.IsActive)
        {
            return ServiceResult<TokenResponse>.Fail("INVALID_REFRESH_TOKEN", "Refresh token is invalid or expired.");
        }

        var accessToken = _jwtTokenService.GenerateAccessToken(
            session.User,
            session.Id,
            out var accessTokenExpiresAtUtc,
            out var jti);

        var newRefreshToken = _jwtTokenService.GenerateRefreshToken();
        session.AccessTokenJti = jti;
        session.RefreshTokenHash = _jwtTokenService.HashToken(newRefreshToken);
        session.ExpiresAtUtc = DateTime.UtcNow.AddDays(Math.Max(_jwtOptions.RefreshTokenDays, 1));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<TokenResponse>.Ok(
            new TokenResponse(
                accessToken,
                newRefreshToken,
                accessTokenExpiresAtUtc,
                ToUserSummary(session.User),
                DeviceBound: session.User.Role != SystemRole.Student || await IsStudentBoundAsync(session.User.Id, cancellationToken)));
    }

    public async Task<ServiceResult<bool>> LogoutAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await _dbContext.UserSessions.FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<bool>.Fail("NOT_FOUND", "Session not found.");
        }

        if (session.RevokedAtUtc is null)
        {
            session.RevokedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<bool>.Ok(true, "Logged out successfully.");
    }

    public Task<ServiceResult<UserSummaryDto>> CreateTeacherAsync(
        Guid institutionId,
        CreateUserRequest request,
        CancellationToken cancellationToken) =>
        CreateUserAsync(institutionId, request, SystemRole.Teacher, cancellationToken);

    public Task<ServiceResult<UserSummaryDto>> CreateStudentAsync(
        Guid institutionId,
        CreateUserRequest request,
        CancellationToken cancellationToken) =>
        CreateUserAsync(institutionId, request, SystemRole.Student, cancellationToken);

    public async Task<ServiceResult<BatchUploadResultDto>> BatchCreateStudentsAsync(
        Guid institutionId,
        Stream csvStream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(csvStream, leaveOpen: true);

        var existingUsernames = await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId)
            .Select(x => x.Username.ToLower())
            .ToListAsync(cancellationToken);

        var usernames = existingUsernames.ToHashSet();
        var errors = new List<string>();
        var created = 0;
        var skipped = 0;
        var lineIndex = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineIndex++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = line.Split(',', StringSplitOptions.TrimEntries);
            if (lineIndex == 1 && columns.Length >= 3 && columns[0].Equals("username", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (columns.Length < 3)
            {
                errors.Add($"Line {lineIndex}: expected username,email,password.");
                skipped++;
                continue;
            }

            var username = columns[0];
            var email = columns[1];
            var password = columns[2];

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                errors.Add($"Line {lineIndex}: contains empty values.");
                skipped++;
                continue;
            }

            var normalizedUsername = username.ToLowerInvariant();
            if (usernames.Contains(normalizedUsername))
            {
                errors.Add($"Line {lineIndex}: username '{username}' already exists.");
                skipped++;
                continue;
            }

            usernames.Add(normalizedUsername);
            _dbContext.Users.Add(new User
            {
                InstitutionId = institutionId,
                Username = username,
                Email = email.ToLowerInvariant(),
                PasswordHash = _passwordService.HashPassword(password),
                Role = SystemRole.Student,
                IsActive = true
            });
            created++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<BatchUploadResultDto>.Ok(
            new BatchUploadResultDto(created, skipped, errors),
            "Batch upload completed.");
    }

    public async Task<IReadOnlyList<StudentBindingStatusDto>> GetStudentBindingStatusesAsync(
        Guid institutionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId && x.Role == SystemRole.Student)
            .OrderBy(x => x.Username)
            .Select(x => new StudentBindingStatusDto(
                x.Id,
                x.Username,
                x.Email,
                x.DeviceBindings.Any(),
                x.DeviceBindings.Select(b => (DateTime?)b.BoundAtUtc).FirstOrDefault(),
                x.DeviceBindings.Select(b => (DateTime?)b.LastSeenAtUtc).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<ServiceResult<bool>> ResetStudentBindingAsync(
        Guid institutionId,
        Guid studentId,
        CancellationToken cancellationToken)
    {
        var studentExists = await _dbContext.Users.AnyAsync(
            x => x.Id == studentId && x.InstitutionId == institutionId && x.Role == SystemRole.Student,
            cancellationToken);

        if (!studentExists)
        {
            return ServiceResult<bool>.Fail("NOT_FOUND", "Student not found.");
        }

        var binding = await _dbContext.DeviceBindings.FirstOrDefaultAsync(
            x => x.StudentUserId == studentId,
            cancellationToken);

        if (binding is not null)
        {
            _dbContext.DeviceBindings.Remove(binding);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<bool>.Ok(true, "Device binding reset successfully.");
    }

    public async Task<ServiceResult<bool>> ForceTerminateStudentSessionsAsync(
        Guid institutionId,
        Guid studentId,
        CancellationToken cancellationToken)
    {
        var studentExists = await _dbContext.Users.AnyAsync(
            x => x.Id == studentId && x.InstitutionId == institutionId && x.Role == SystemRole.Student,
            cancellationToken);

        if (!studentExists)
        {
            return ServiceResult<bool>.Fail("NOT_FOUND", "Student not found.");
        }

        var sessions = await _dbContext.UserSessions
            .Where(x => x.UserId == studentId && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAtUtc = DateTime.UtcNow;
        }

        if (sessions.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<bool>.Ok(true, "Active sessions terminated.");
    }

    private async Task<ServiceResult<UserSummaryDto>> CreateUserAsync(
        Guid institutionId,
        CreateUserRequest request,
        SystemRole role,
        CancellationToken cancellationToken)
    {
        var institutionExists = await _dbContext.Institutions.AnyAsync(x => x.Id == institutionId, cancellationToken);
        if (!institutionExists)
        {
            return ServiceResult<UserSummaryDto>.Fail("INSTITUTION_NOT_FOUND", "Institution not found.");
        }

        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        var userExists = await _dbContext.Users.AnyAsync(
            x => x.InstitutionId == institutionId &&
                 (x.Username.ToLower() == username.ToLower() || x.Email.ToLower() == email),
            cancellationToken);

        if (userExists)
        {
            return ServiceResult<UserSummaryDto>.Fail("USER_EXISTS", "A user with this username or email already exists.");
        }

        var user = new User
        {
            InstitutionId = institutionId,
            Username = username,
            Email = email,
            PasswordHash = _passwordService.HashPassword(request.Password),
            Role = role,
            IsActive = true
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<UserSummaryDto>.Ok(ToUserSummary(user), $"{role} account created.");
    }

    private async Task<ServiceResult<TokenResponse>> IssueTokensAsync(
        User user,
        bool deviceBound,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var accessToken = _jwtTokenService.GenerateAccessToken(
            user,
            sessionId,
            out var accessTokenExpiresAtUtc,
            out var jti);

        var refreshToken = _jwtTokenService.GenerateRefreshToken();
        var refreshTokenHash = _jwtTokenService.HashToken(refreshToken);

        _dbContext.UserSessions.Add(new UserSession
        {
            Id = sessionId,
            UserId = user.Id,
            AccessTokenJti = jti,
            RefreshTokenHash = refreshTokenHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(Math.Max(_jwtOptions.RefreshTokenDays, 1))
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<TokenResponse>.Ok(
            new TokenResponse(
                accessToken,
                refreshToken,
                accessTokenExpiresAtUtc,
                ToUserSummary(user),
                deviceBound));
    }

    private async Task<User?> FindUserByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken cancellationToken)
    {
        var normalized = usernameOrEmail.Trim().ToLowerInvariant();

        return await _dbContext.Users.FirstOrDefaultAsync(
            x => x.Username.ToLower() == normalized || x.Email.ToLower() == normalized,
            cancellationToken);
    }

    private static UserSummaryDto ToUserSummary(User user) =>
        new UserSummaryDto(user.Id, user.InstitutionId, user.Username, user.Email, user.Role);

    private async Task<bool> IsStudentBoundAsync(Guid studentId, CancellationToken cancellationToken) =>
        await _dbContext.DeviceBindings.AnyAsync(x => x.StudentUserId == studentId, cancellationToken);
}
