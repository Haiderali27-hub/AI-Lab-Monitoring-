using Backend_API.Contracts;
using Backend_API.Data;
using Backend_API.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services;

public class InstitutionService(AppDbContext dbContext) : IInstitutionService
{
    private readonly AppDbContext _dbContext = dbContext;

    public async Task<ServiceResult<InstitutionDetailDto>> GetInstitutionAsync(Guid institutionId, CancellationToken cancellationToken)
    {
        var inst = await _dbContext.Institutions.FirstOrDefaultAsync(x => x.Id == institutionId, cancellationToken);
        if (inst == null) return ServiceResult<InstitutionDetailDto>.Fail("NOT_FOUND", "Institution not found.");

        return ServiceResult<InstitutionDetailDto>.Ok(MapToDto(inst));
    }

    public async Task<ServiceResult<InstitutionDetailDto>> UpdateInstitutionAsync(Guid institutionId, UpdateInstitutionRequest request, CancellationToken cancellationToken)
    {
        var inst = await _dbContext.Institutions.FirstOrDefaultAsync(x => x.Id == institutionId, cancellationToken);
        if (inst == null) return ServiceResult<InstitutionDetailDto>.Fail("NOT_FOUND", "Institution not found.");

        inst.Name = request.Name;
        inst.ContactEmail = request.ContactEmail;
        inst.LogoUrl = request.LogoUrl;
        inst.AllowedIpRanges = request.AllowedIpRanges;
        inst.EnforceSingleDeviceBinding = request.EnforceSingleDeviceBinding;
        inst.AllowTeacherResetBinding = request.AllowTeacherResetBinding;
        inst.SessionTimeoutMinutes = request.SessionTimeoutMinutes;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<InstitutionDetailDto>.Ok(MapToDto(inst));
    }

    public async Task<IReadOnlyList<LabDto>> GetLabsAsync(Guid institutionId, CancellationToken cancellationToken)
    {
        return await _dbContext.Labs
            .Where(x => x.InstitutionId == institutionId)
            .Select(x => new LabDto(x.Id, x.Name, x.RegisteredTerminals, x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<ServiceResult<LabDto>> CreateLabAsync(Guid institutionId, CreateLabRequest request, CancellationToken cancellationToken)
    {
        var lab = new Lab
        {
            InstitutionId = institutionId,
            Name = request.Name,
            RegisteredTerminals = request.RegisteredTerminals,
            IsActive = true
        };

        _dbContext.Labs.Add(lab);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<LabDto>.Ok(new LabDto(lab.Id, lab.Name, lab.RegisteredTerminals, lab.IsActive));
    }

    public async Task<ServiceResult<bool>> DeleteLabAsync(Guid institutionId, Guid labId, CancellationToken cancellationToken)
    {
        var lab = await _dbContext.Labs.FirstOrDefaultAsync(x => x.Id == labId && x.InstitutionId == institutionId, cancellationToken);
        if (lab == null) return ServiceResult<bool>.Fail("NOT_FOUND", "Lab not found.");

        _dbContext.Labs.Remove(lab);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Ok(true);
    }

    private static InstitutionDetailDto MapToDto(Institution inst) =>
        new(inst.Id, inst.Name, inst.ContactEmail, inst.LogoUrl, inst.AllowedIpRanges, 
            inst.EnforceSingleDeviceBinding, inst.AllowTeacherResetBinding, inst.SessionTimeoutMinutes, inst.CreatedAtUtc);
}
