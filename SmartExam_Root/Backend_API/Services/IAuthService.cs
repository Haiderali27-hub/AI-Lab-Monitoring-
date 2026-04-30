using Backend_API.Contracts;

namespace Backend_API.Services;

public interface IAuthService
{
    Task<ServiceResult<TokenResponse>> BootstrapSuperAdminAsync(BootstrapAdminRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<TokenResponse>> BootstrapAdminAsync(BootstrapAdminRequest request, CancellationToken cancellationToken);
    Task<bool> IsBootstrappedAsync(CancellationToken cancellationToken);
    Task<ServiceResult<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<TokenResponse>> StudentLoginAsync(StudentLoginRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<TokenResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<bool>> LogoutAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<ServiceResult<UserSummaryDto>> CreateTeacherAsync(Guid institutionId, CreateUserRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<UserSummaryDto>> CreateStudentAsync(Guid institutionId, CreateUserRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<BatchUploadResultDto>> BatchCreateStudentsAsync(Guid institutionId, Stream csvStream, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudentBindingStatusDto>> GetStudentBindingStatusesAsync(Guid institutionId, CancellationToken cancellationToken);
    Task<ServiceResult<bool>> ResetStudentBindingAsync(Guid institutionId, Guid studentId, CancellationToken cancellationToken);
    Task<ServiceResult<bool>> ForceTerminateStudentSessionsAsync(Guid institutionId, Guid studentId, CancellationToken cancellationToken);
}