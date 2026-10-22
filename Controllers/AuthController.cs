using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;
using PickleballBookingSystem.Middleware;

namespace PickleballBookingSystem.Controllers;

[ApiController, Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IClientService _clientService;
    private readonly IConfiguration _config;

    public AuthController(
        IAuthService auth,
        IClientService clientService,
        IConfiguration config)
    {
        _auth = auth;
        _clientService = clientService;
        _config = config;
    }

    private async Task<Guid> GetClientId()
    {
        var subdomain = HttpContext.Items["Subdomain"] as string
                        ?? ClientResolver.ResolveSubdomain(HttpContext, _config);
        if (string.IsNullOrEmpty(subdomain))
            throw new UnauthorizedAccessException("Client identification required");
        return await _clientService.GetClientIdBySubdomainAsync(subdomain);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        // Pass IP + UA for security logging
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = HttpContext.Request.Headers["User-Agent"].FirstOrDefault();
        return Ok(await _auth.LoginAsync(request, ip, ua));
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var clientId = await GetClientId();
        return Ok(await _auth.RegisterAsync(request, clientId));
    }

    [HttpPost("register-staff")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<AuthResponse>> RegisterStaff(RegisterRequest request)
    {
        var clientId = await GetClientId();
        return Ok(await _auth.RegisterStaffAsync(request, clientId));
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        await _auth.ForgotPasswordAsync(request.Email);
        return Ok(new { message = "If an account exists, a reset link has been sent" });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        return Ok(new { message = "Password reset successfully" });
    }

    [Authorize, HttpPut("profile")]
    public async Task<ActionResult<UserDto>> UpdateProfile(UpdateProfileRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await _auth.UpdateProfileAsync(userId, request));
    }

    [Authorize, HttpPut("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _auth.ChangePasswordAsync(userId, request);
        return Ok(new { message = "Password changed successfully" });
    }
}