using Backend_API.Contracts;

namespace Backend_API.Services;

public interface IInstitutionService
{
    Task<ServiceResult<InstitutionDetailDto>> GetInstitutionAsync(Guid institutionId, CancellationToken cancellationToken);
    Task<ServiceResult<InstitutionDetailDto>> UpdateInstitutionAsync(Guid institutionId, UpdateInstitutionRequest request, CancellationToken cancellationToken);
    
    Task<IReadOnlyList<LabDto>> GetLabsAsync(Guid institutionId, CancellationToken cancellationToken);
    Task<ServiceResult<LabDto>> CreateLabAsync(Guid institutionId, CreateLabRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<bool>> DeleteLabAsync(Guid institutionId, Guid labId, CancellationToken cancellationToken);
}
