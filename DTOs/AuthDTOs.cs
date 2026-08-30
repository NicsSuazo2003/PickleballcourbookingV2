namespace PickleballBookingSystem.DTOs;

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Name, string Email, string Phone, string Password);
public record ForgotPasswordRequest(string Email);
public record UpdateProfileRequest(string? Name, string? Email, string? Phone);
public record AuthResponse(string Token, UserDto User);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record UserDto(
    string Id,
    string Name,
    string Email,
    string Phone,
    string Role,
    string? Avatar,
    string CreatedAt,
    int BookingsCount,
    string Status
);