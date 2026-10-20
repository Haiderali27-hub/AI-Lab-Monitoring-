using Backend_API.Models.Enums;

namespace Backend_API.DTOs.Users;

// RollNumber (Module 1) is optional; it is stored trimmed + upper-case and must be unique.
public record CreateUserRequest(string Name, string Email, string Password, UserRole Role, string? RollNumber = null);
