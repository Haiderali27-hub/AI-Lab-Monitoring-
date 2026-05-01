using Backend_API.Contracts;

namespace Backend_API.Services;

public interface IInstitutionService
{
    Task<ServiceResult<InstitutionDetailDto>> GetInstitutionAsync(Guid institutionId, CancellationToken cancellationToken);
    Task<ServiceResult<InstitutionDetailDto>> UpdateInstitutionAsync(Guid institutionId, UpdateInstitutionRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<LabDto>> GetLabsAsync(Guid institutionId, CancellationToken cancellationToken);
    Task<ServiceResult<LabDto>> CreateLabAsync(Guid institutionId, CreateLabRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<bool>> DeleteLabAsync(Guid institutionId, Guid labId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkstationDto>> GetWorkstationsAsync(Guid institutionId, Guid labId, CancellationToken cancellationToken);
    Task<ServiceResult<WorkstationDto>> CreateWorkstationAsync(Guid institutionId, Guid labId, CreateWorkstationRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<WorkstationDto>> UpdateWorkstationAsync(Guid institutionId, Guid labId, Guid workstationId, UpdateWorkstationRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<bool>> DeleteWorkstationAsync(Guid institutionId, Guid labId, Guid workstationId, CancellationToken cancellationToken);
}
