using System.Security.Claims;
using Backend_API.Contracts;
using Backend_API.Models;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/institution")]
[Authorize]
public class InstitutionController(IInstitutionService institutionService) : ControllerBase
{
    private readonly IInstitutionService _institutionService = institutionService;

    [HttpGet("settings")]
    [Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.SuperAdmin)}")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var result = await _institutionService.GetInstitutionAsync(institutionId, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpPut("settings")]
    [Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.SuperAdmin)}")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateInstitutionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var result = await _institutionService.UpdateInstitutionAsync(institutionId, request, cancellationToken);
        return this.FromServiceResult(result);
    }

    // Labs

    [HttpGet("labs")]
    [Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.SuperAdmin)},{nameof(SystemRole.Teacher)}")]
    public async Task<IActionResult> GetLabs(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var data = await _institutionService.GetLabsAsync(institutionId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<LabDto>>.Ok(data));
    }

    [HttpPost("labs")]
    [Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.SuperAdmin)}")]
    public async Task<IActionResult> CreateLab([FromBody] CreateLabRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var result = await _institutionService.CreateLabAsync(institutionId, request, cancellationToken);
        return this.FromServiceResult(result, StatusCodes.Status201Created);
    }

    [HttpDelete("labs/{labId:guid}")]
    [Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.SuperAdmin)}")]
    public async Task<IActionResult> DeleteLab(Guid labId, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var result = await _institutionService.DeleteLabAsync(institutionId, labId, cancellationToken);
        return this.FromServiceResult(result);
    }

    // Workstations

    [HttpGet("labs/{labId:guid}/workstations")]
    [Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.SuperAdmin)},{nameof(SystemRole.Teacher)}")]
    public async Task<IActionResult> GetWorkstations(Guid labId, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var data = await _institutionService.GetWorkstationsAsync(institutionId, labId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<WorkstationDto>>.Ok(data));
    }

    [HttpPost("labs/{labId:guid}/workstations")]
    [Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.SuperAdmin)}")]
    public async Task<IActionResult> CreateWorkstation(Guid labId, [FromBody] CreateWorkstationRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var result = await _institutionService.CreateWorkstationAsync(institutionId, labId, request, cancellationToken);
        return this.FromServiceResult(result, StatusCodes.Status201Created);
    }

    [HttpPut("labs/{labId:guid}/workstations/{workstationId:guid}")]
    [Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.SuperAdmin)}")]
    public async Task<IActionResult> UpdateWorkstation(Guid labId, Guid workstationId, [FromBody] UpdateWorkstationRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var result = await _institutionService.UpdateWorkstationAsync(institutionId, labId, workstationId, request, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpDelete("labs/{labId:guid}/workstations/{workstationId:guid}")]
    [Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.SuperAdmin)}")]
    public async Task<IActionResult> DeleteWorkstation(Guid labId, Guid workstationId, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var result = await _institutionService.DeleteWorkstationAsync(institutionId, labId, workstationId, cancellationToken);
        return this.FromServiceResult(result);
    }

    private bool TryGetInstitutionId(out Guid institutionId)
    {
        var claimValue = User.FindFirstValue("institutionId");
        return Guid.TryParse(claimValue, out institutionId);
    }
}
