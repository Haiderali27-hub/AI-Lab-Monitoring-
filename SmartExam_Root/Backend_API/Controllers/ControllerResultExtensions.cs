using Backend_API.Contracts;
using Backend_API.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend_API.Controllers;

public static class ControllerResultExtensions
{
    public static IActionResult FromServiceResult<T>(this ControllerBase controller, ServiceResult<T> result, int successStatusCode = StatusCodes.Status200OK)
    {
        if (result.Success)
        {
            return controller.StatusCode(
                successStatusCode,
                ApiResponse<T>.Ok(result.Data!, result.Message, result.Code));
        }

        var statusCode = result.Code switch
        {
            "INVALID_CREDENTIALS" => StatusCodes.Status401Unauthorized,
            "INVALID_REFRESH_TOKEN" => StatusCodes.Status401Unauthorized,
            "DEVICE_MISMATCH" => StatusCodes.Status403Forbidden,
            "ACTIVE_SESSION_EXISTS" => StatusCodes.Status409Conflict,
            "BOOTSTRAP_ALREADY_COMPLETED" => StatusCodes.Status409Conflict,
            "USER_EXISTS" => StatusCodes.Status409Conflict,
            "NOT_FOUND" => StatusCodes.Status404NotFound,
            "INSTITUTION_NOT_FOUND" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };

        return controller.StatusCode(statusCode, ApiResponse<T>.Fail(result.Code, result.Message));
    }
}