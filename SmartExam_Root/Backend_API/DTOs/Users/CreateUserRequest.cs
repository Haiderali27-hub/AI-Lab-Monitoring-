using Backend_API.Models.Enums;

namespace Backend_API.DTOs.Users;

public record CreateUserRequest(string Name, string Email, string Password, UserRole Role);
