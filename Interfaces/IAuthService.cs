using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RegisterAsync(RegisterRequest request, Guid clientId);
    Task<AuthResponse> RegisterStaffAsync(RegisterRequest request, Guid clientId);
    Task ForgotPasswordAsync(string email);                              // existing signature kept
    Task ResetPasswordAsync(string token, string newPassword);           // 👈 NEW
    Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
}