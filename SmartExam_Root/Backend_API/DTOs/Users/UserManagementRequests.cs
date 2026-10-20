using Backend_API.Models.Enums;

namespace Backend_API.DTOs.Users;

// Edit an existing user. Password is optional — omit/blank to keep the current one.
// RollNumber: null = unchanged, "" = clear, otherwise set (unique, normalised to upper-case).
public record UpdateUserRequest(string Name, string Email, UserRole Role, string? Password = null, string? RollNumber = null);

// One row of a batch import (frontend parses a CSV into this shape).
public record ImportUserRow(string Name, string Email, string Password, UserRole Role, string? RollNumber = null);

public record ImportUsersRequest(List<ImportUserRow> Users);
