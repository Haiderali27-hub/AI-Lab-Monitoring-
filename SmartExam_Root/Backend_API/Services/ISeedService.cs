using Backend_API.Contracts;

namespace Backend_API.Services;

public interface ISeedService
{
    Task EnsureSuperAdminAsync(CancellationToken cancellationToken);
    Task<ServiceResult<SeedDemoResponse>> SeedDemoAsync(Guid institutionId, SeedDemoRequest request, CancellationToken cancellationToken);
}