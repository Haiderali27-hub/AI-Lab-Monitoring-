namespace Backend_API.DTOs.Auth;

public record LoginRequest(string Email, string Password, string? HwidHash = null);
