using System.Security.Claims;
using Backend_API.Contracts;
using Backend_API.Models;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/institution")]
[Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.SuperAdmin)}")]
public class InstitutionController(IInstitutionService institutionService) : ControllerBase
{
    private readonly IInstitutionService _institutionService = institutionService;

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var result = await _institutionService.GetInstitutionAsync(institutionId, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateInstitutionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var result = await _institutionService.UpdateInstitutionAsync(institutionId, request, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpGet("labs")]
    public async Task<IActionResult> GetLabs(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var data = await _institutionService.GetLabsAsync(institutionId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<LabDto>>.Ok(data));
    }

    [HttpPost("labs")]
    public async Task<IActionResult> CreateLab([FromBody] CreateLabRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var result = await _institutionService.CreateLabAsync(institutionId, request, cancellationToken);
        return this.FromServiceResult(result, StatusCodes.Status201Created);
    }

    [HttpDelete("labs/{labId:guid}")]
    public async Task<IActionResult> DeleteLab(Guid labId, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId)) return Unauthorized();
        var result = await _institutionService.DeleteLabAsync(institutionId, labId, cancellationToken);
        return this.FromServiceResult(result);
    }

    private bool TryGetInstitutionId(out Guid institutionId)
    {
        var claimValue = User.FindFirstValue("institutionId");
        return Guid.TryParse(claimValue, out institutionId);
    }
}
