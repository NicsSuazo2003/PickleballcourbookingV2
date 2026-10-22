using System.ComponentModel.DataAnnotations;

namespace PickleballBookingSystem.DTOs;

public record LoginRequest(
    [Required, EmailAddress, MaxLength(120)] string Email,
    [Required, MaxLength(128)] string Password
);

public record RegisterRequest(
    [Required, MaxLength(80)] string Name,
    [Required, EmailAddress, MaxLength(120)] string Email,
    [Phone, MaxLength(20)] string? Phone,
    [Required, MinLength(8), MaxLength(128)] string Password
);

public record AuthResponse(string Token, UserDto User);

public record ForgotPasswordRequest(
    [Required, EmailAddress, MaxLength(120)] string Email
);

public record ResetPasswordRequest(
    [Required, MinLength(16), MaxLength(200)] string Token,
    [Required, MinLength(8), MaxLength(128)] string NewPassword
);

public record ChangePasswordRequest(
    [Required, MaxLength(128)] string CurrentPassword,
    [Required, MinLength(8), MaxLength(128)] string NewPassword
);

public record UpdateProfileRequest(
    [MaxLength(80)] string? Name,
    [EmailAddress, MaxLength(120)] string? Email,
    [Phone, MaxLength(20)] string? Phone
);