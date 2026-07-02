namespace Backend_API.DTOs.Auth;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
