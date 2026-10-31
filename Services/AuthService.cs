using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Entities;
using PickleballBookingSystem.Interfaces;
using BCrypt.Net;

namespace PickleballBookingSystem.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokenService;
    private readonly EmailService _email;
    private readonly ILogger<AuthService> _logger;
    private readonly ISecurityLogger _security;

    private const int ResetTokenExpiryMinutes = 60;
    private const int MaxFailedAttempts = 10;
    private const int LockoutMinutes = 30;
    private static readonly string[] PasswordResettableRoles = { "admin", "staff" };

    // Pre-computed bcrypt hash used to keep login timing constant even when user not found.
    private static readonly string DummyHash =
        "$2a$12$C6UzMDM.H6dfI/f/IKcEeO1QPR6SxT9jVqUoD3m7Y0aJ1yZ9bK9j2";

    public AuthService(
        AppDbContext db,
        TokenService tokenService,
        EmailService email,
        ILogger<AuthService> logger,
        ISecurityLogger security)
    {
        _db = db;
        _tokenService = tokenService;
        _email = email;
        _logger = logger;
        _security = security;
    }

    private static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// Best-effort ClientId resolution for logging when the user was not found.
    /// Keeps security events attributable to a tenant so they show up in the admin UI.
    /// Falls back to Guid.Empty only when no user with that email exists in any tenant.
    /// </summary>
    private async Task<Guid> ResolveClientIdForLogAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return Guid.Empty;

        var uid = await _db.Users
            .Where(u => u.Email.ToLower() == email)
            .Select(u => (Guid?)u.ClientId)
            .FirstOrDefaultAsync();

        return uid ?? Guid.Empty;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ip = null, string? ua = null)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);

        // ── Wrong email OR wrong password (constant-time via dummy hash) ──
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash ?? DummyHash))
        {
            // 🔒 FIX (Bug B): increment failed count & lock out when threshold hit.
            if (user is not null)
            {
                user.FailedLoginCount += 1;

                if (user.FailedLoginCount >= MaxFailedAttempts)
                {
                    user.LockoutUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                }

                await _db.SaveChangesAsync();
            }

            // 🔒 FIX (Bug C): log with the real ClientId when we know it.
            var logClientId = user?.ClientId
                ?? await ResolveClientIdForLogAsync(email);

            await _security.LogAsync(new SecurityEvent
            {
                ClientId = logClientId,
                ActorEmail = email,
                IpHash = _security.HashIp(ip),
                IpAddress = ip,
                UserAgent = ua,
                EventType = "auth.login_failed",
                Severity = "warning",
                Message = $"Failed login for {email}",
                Success = false,
            });
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        // ── Locked account ──
        if (user.LockoutUntil > DateTime.UtcNow)
        {
            await _security.LogAsync(new SecurityEvent
            {
                ClientId = user.ClientId,
                ActorUserId = user.Id,
                ActorEmail = email,
                IpHash = _security.HashIp(ip),
                IpAddress = ip,
                UserAgent = ua,
                EventType = "auth.login_locked_out",
                Severity = "warning",
                Message = $"Login on locked account: {email}",
                Success = false,
            });
            throw new UnauthorizedAccessException("Too many failed attempts. Try again later.");
        }

        // ── Success: clear lockout state ──
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        await _db.SaveChangesAsync();

        await _security.LogAsync(new SecurityEvent
        {
            ClientId = user.ClientId,
            ActorUserId = user.Id,
            ActorEmail = email,
            IpHash = _security.HashIp(ip),
            IpAddress = ip,
            UserAgent = ua,
            EventType = "auth.login_success",
            Severity = "info",
            Message = $"Login succeeded: {email}",
        });

        return new AuthResponse(_tokenService.GenerateToken(user), MapToDto(user));
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, Guid clientId)
    {
        var email = NormalizeEmail(request.Email);

        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == email))
            throw new InvalidOperationException("An account with this email already exists");

        var user = new User
        {
            Name = request.Name.Trim(),
            Email = email,
            Phone = request.Phone?.Trim() ?? string.Empty,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = "user",
            ClientId = clientId,
            CreatedAt = DateTime.UtcNow,
            Status = "active",
            FailedLoginCount = 0,
            LockoutUntil = null,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return new AuthResponse(_tokenService.GenerateToken(user), MapToDto(user));
    }

    public async Task<AuthResponse> RegisterStaffAsync(RegisterRequest request, Guid clientId)
    {
        var email = NormalizeEmail(request.Email);

        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == email))
            throw new InvalidOperationException("An account with this email already exists");

        var user = new User
        {
            Name = request.Name.Trim(),
            Email = email,
            Phone = request.Phone?.Trim() ?? string.Empty,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = "staff",
            ClientId = clientId,
            CreatedAt = DateTime.UtcNow,
            Status = "active",
            FailedLoginCount = 0,
            LockoutUntil = null,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return new AuthResponse(_tokenService.GenerateToken(user), MapToDto(user));
    }

    public async Task ForgotPasswordAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return;

        var normalized = NormalizeEmail(email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalized);

        if (user == null) return;
        if (!PasswordResettableRoles.Contains(user.Role, StringComparer.OrdinalIgnoreCase)) return;

        var rawToken = GenerateUrlSafeToken(32);
        var hash = Sha256Hex(rawToken);

        user.PasswordResetTokenHash = hash;
        user.PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(ResetTokenExpiryMinutes);
        await _db.SaveChangesAsync();

        try
        {
            await _email.SendPasswordResetEmailAsync(
                user.Name, user.Email, rawToken, ResetTokenExpiryMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password reset email failed for {UserId}", user.Id);
        }
    }

    public async Task ResetPasswordAsync(string token, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Reset link is invalid or expired");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new InvalidOperationException("Password must be at least 8 characters");

        var hash = Sha256Hex(token.Trim());
        var now = DateTime.UtcNow;

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.PasswordResetTokenHash == hash &&
            u.PasswordResetExpiresAt != null &&
            u.PasswordResetExpiresAt > now);

        if (user == null)
            throw new InvalidOperationException("Reset link is invalid or expired");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetExpiresAt = null;
        // 🔒 Clear lockout on successful password reset.
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        await _db.SaveChangesAsync();

        // 🔒 NEW: emit auth.password_changed (previously missing).
        await _security.LogAsync(new SecurityEvent
        {
            ClientId = user.ClientId,
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            EventType = "auth.password_changed",
            Severity = "info",
            Message = $"Password reset completed for {user.Email}",
        });
    }

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        if (!string.IsNullOrWhiteSpace(request.Name)) user.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var newEmail = NormalizeEmail(request.Email);
            if (await _db.Users.AnyAsync(u => u.Id != userId && u.Email.ToLower() == newEmail))
                throw new InvalidOperationException("That email is already in use.");
            user.Email = newEmail;
        }
        if (!string.IsNullOrWhiteSpace(request.Phone)) user.Phone = request.Phone.Trim();

        await _db.SaveChangesAsync();
        return MapToDto(user);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect");

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            throw new InvalidOperationException("New password must be at least 8 characters");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        // 🔒 Successful password change clears any lockout state.
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        await _db.SaveChangesAsync();

        // 🔒 NEW: emit auth.password_changed (previously missing).
        await _security.LogAsync(new SecurityEvent
        {
            ClientId = user.ClientId,
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            EventType = "auth.password_changed",
            Severity = "info",
            Message = $"Password changed for {user.Email}",
        });
    }

    private static string GenerateUrlSafeToken(int byteLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static UserDto MapToDto(User u) => new(
        u.Id.ToString(), u.Name, u.Email, u.Phone, u.Role, u.Avatar,
        u.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"), u.BookingsCount, u.Status);
}