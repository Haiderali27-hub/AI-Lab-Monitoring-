using System.Security.Claims;
using Backend_API.Contracts;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    private readonly IAuthService _authService = authService;

    [HttpGet("bootstrap-status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBootstrapStatus(CancellationToken cancellationToken)
    {
        var isBootstrapped = await _authService.IsBootstrappedAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { isBootstrapped }));
    }

    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<IActionResult> Setup(
        [FromBody] BootstrapAdminRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.BootstrapAdminAsync(request, cancellationToken);
        return this.FromServiceResult(result, StatusCodes.Status201Created);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpPost("student-login")]
    [AllowAnonymous]
    public async Task<IActionResult> StudentLogin(
        [FromBody] StudentLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.StudentLoginAsync(request, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshAsync(request, cancellationToken);
        return this.FromServiceResult(result);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var sessionClaim = User.FindFirstValue(ClaimTypes.Sid);
        if (!Guid.TryParse(sessionClaim, out var sessionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Session claim is missing."));
        }

        var result = await _authService.LogoutAsync(sessionId, cancellationToken);
        return this.FromServiceResult(result);
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);
        var institutionId = User.FindFirstValue("institutionId");

        return Ok(ApiResponse<object>.Ok(new
        {
            userId,
            role,
            institutionId
        }));
    }
}
