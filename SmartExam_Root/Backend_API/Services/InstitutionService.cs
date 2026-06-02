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

    public async Task<IReadOnlyList<WorkstationDto>> GetWorkstationsAsync(Guid institutionId, Guid labId, CancellationToken cancellationToken)
    {
        var labExists = await _dbContext.Labs.AnyAsync(x => x.Id == labId && x.InstitutionId == institutionId, cancellationToken);
        if (!labExists) return Array.Empty<WorkstationDto>();

        return await _dbContext.Workstations
            .Where(x => x.LabId == labId)
            .OrderBy(x => x.Name)
            .Select(x => new WorkstationDto(x.Id, x.LabId, x.Name, x.IpAddress, x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<ServiceResult<WorkstationDto>> CreateWorkstationAsync(Guid institutionId, Guid labId, CreateWorkstationRequest request, CancellationToken cancellationToken)
    {
        var labExists = await _dbContext.Labs.AnyAsync(x => x.Id == labId && x.InstitutionId == institutionId, cancellationToken);
        if (!labExists) return ServiceResult<WorkstationDto>.Fail("LAB_NOT_FOUND", "Lab not found.");

        var duplicate = await _dbContext.Workstations.AnyAsync(x => x.LabId == labId && x.Name == request.Name.Trim(), cancellationToken);
        if (duplicate) return ServiceResult<WorkstationDto>.Fail("DUPLICATE_NAME", "A workstation with this name already exists in the lab.");

        var workstation = new Workstation
        {
            LabId = labId,
            Name = request.Name.Trim(),
            IpAddress = string.IsNullOrWhiteSpace(request.IpAddress) ? null : request.IpAddress.Trim(),
            IsActive = true
        };

        _dbContext.Workstations.Add(workstation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<WorkstationDto>.Ok(new WorkstationDto(workstation.Id, workstation.LabId, workstation.Name, workstation.IpAddress, workstation.IsActive));
    }

    public async Task<ServiceResult<WorkstationDto>> UpdateWorkstationAsync(Guid institutionId, Guid labId, Guid workstationId, UpdateWorkstationRequest request, CancellationToken cancellationToken)
    {
        var workstation = await _dbContext.Workstations
            .Include(x => x.Lab)
            .FirstOrDefaultAsync(x => x.Id == workstationId && x.LabId == labId && x.Lab.InstitutionId == institutionId, cancellationToken);

        if (workstation == null) return ServiceResult<WorkstationDto>.Fail("NOT_FOUND", "Workstation not found.");

        var duplicate = await _dbContext.Workstations.AnyAsync(
            x => x.LabId == labId && x.Name == request.Name.Trim() && x.Id != workstationId,
            cancellationToken);
        if (duplicate) return ServiceResult<WorkstationDto>.Fail("DUPLICATE_NAME", "A workstation with this name already exists in the lab.");

        workstation.Name = request.Name.Trim();
        workstation.IpAddress = string.IsNullOrWhiteSpace(request.IpAddress) ? null : request.IpAddress.Trim();
        workstation.IsActive = request.IsActive;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<WorkstationDto>.Ok(new WorkstationDto(workstation.Id, workstation.LabId, workstation.Name, workstation.IpAddress, workstation.IsActive));
    }

    public async Task<ServiceResult<bool>> DeleteWorkstationAsync(Guid institutionId, Guid labId, Guid workstationId, CancellationToken cancellationToken)
    {
        var workstation = await _dbContext.Workstations
            .Include(x => x.Lab)
            .FirstOrDefaultAsync(x => x.Id == workstationId && x.LabId == labId && x.Lab.InstitutionId == institutionId, cancellationToken);

        if (workstation == null) return ServiceResult<bool>.Fail("NOT_FOUND", "Workstation not found.");

        _dbContext.Workstations.Remove(workstation);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Ok(true);
    }

    private static InstitutionDetailDto MapToDto(Institution inst) =>
        new(inst.Id, inst.Name, inst.ContactEmail, inst.LogoUrl, inst.AllowedIpRanges,
            inst.EnforceSingleDeviceBinding, inst.AllowTeacherResetBinding, inst.SessionTimeoutMinutes, inst.CreatedAtUtc);
}
