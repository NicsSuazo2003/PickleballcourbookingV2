using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;
using PickleballBookingSystem.Middleware;

namespace PickleballBookingSystem.Controllers;

[ApiController, Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;
    private readonly IBookingService _booking;
    private readonly ICourtService _court;
    private readonly IClientService _clientService;
    private readonly IConfiguration _config;
    private readonly IPricingRuleService _pricingRuleService;
    private readonly AppDbContext _db;

    public AdminController(
        IAdminService admin,
        IBookingService booking,
        ICourtService court,
        IClientService clientService,
        IConfiguration config,
        IPricingRuleService pricingRuleService,
        AppDbContext db)
    {
        _admin = admin;
        _booking = booking;
        _court = court;
        _clientService = clientService;
        _config = config;
        _pricingRuleService = pricingRuleService;
        _db = db;
    }

    /// <summary>
    /// Resolves the effective client for this request AND (🔒 FIX Bug D)
    /// verifies it matches the authenticated user's tenant claim.
    ///
    /// Threat model: X-Client-Subdomain is user-supplied. An admin of tenant A
    /// could set it to tenant B and read/write B's data. We reject any request
    /// where the JWT's client_id does not equal the resolved client.
    ///
    /// Exceptions:
    ///   - Endpoints with no authenticated user (all admin endpoints have [Authorize],
    ///     so this branch is defensive only).
    ///   - If the JWT has no client_id claim (legacy token), we still reject on
    ///     admin endpoints to fail closed.
    /// </summary>
    private async Task<Guid> GetClientId()
    {
        // Resolve tenant from request (header / host / default).
        Guid resolved;
        if (HttpContext.Items["ClientId"] is Guid cached)
        {
            resolved = cached;
        }
        else
        {
            var subdomain = ClientResolver.ResolveSubdomain(HttpContext, _config);
            if (string.IsNullOrEmpty(subdomain))
                throw new UnauthorizedAccessException("Client identification required");
            resolved = await _clientService.GetClientIdBySubdomainAsync(subdomain);
        }

        // 🔒 Tenant consistency check.
        // Only enforced when the user is authenticated — all controller actions
        // carry [Authorize], so in practice this always runs.
        if (User?.Identity?.IsAuthenticated == true)
        {
            var claimRaw = User.FindFirstValue("client_id");
            if (!Guid.TryParse(claimRaw, out var jwtClientId) || jwtClientId != resolved)
            {
                // Do NOT leak the mismatched IDs — generic unauthorized is enough.
                throw new UnauthorizedAccessException("Client identification required");
            }
        }

        return resolved;
    }

    private Guid ActingUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var g) ? g : Guid.Empty;
    }

    // ========================================
    // STAFF & ADMIN
    // ========================================

    [HttpGet("bookings")]
    [Authorize(Roles = "admin,staff")]
    public async Task<ActionResult<List<BookingDto>>> GetBookings(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var clientId = await GetClientId();
        var bookings = await _booking.GetBookingsAsync(clientId, from, to);
        return Ok(bookings);
    }

    [HttpPut("bookings/{id}")]
    [Authorize(Roles = "admin,staff")]
    public async Task<ActionResult<BookingDto>> UpdateBooking(Guid id, AdminUpdateBookingRequest request)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (role == "staff")
        {
            var allowed = new[] { "confirmed", "cancelled" };
            if (!allowed.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
                return Forbid();
        }

        var clientId = await GetClientId();
        var booking = await _booking.AdminUpdateBookingAsync(id, request, clientId);
        return Ok(booking);
    }

    [HttpPost("bookings/manual")]
    [Authorize(Roles = "admin,staff")]
    public async Task<ActionResult<BookingDto>> CreateManualBooking(StaffCreateBookingRequest request)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);

        if (role != "admin")
            request = request with { TotalAmount = null };

        var clientId = await GetClientId();
        var booking = await _booking.CreateStaffBookingAsync(request, clientId);
        return Ok(booking);
    }

    [HttpPatch("bookings/{id}/reschedule")]
    [Authorize(Roles = "admin,staff")]
    public async Task<ActionResult<RescheduleBookingResponse>> RescheduleBooking(
        Guid id, RescheduleBookingRequest request)
    {
        var clientId = await GetClientId();
        var result = await _booking.RescheduleBookingAsync(id, request, clientId);
        return Ok(result);
    }

    // ========================================
    // ADMIN ONLY
    // ========================================

    [HttpGet("analytics")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<AnalyticsDto>> GetAnalytics()
    {
        var clientId = await GetClientId();
        return Ok(await _admin.GetAnalyticsAsync(clientId));
    }

    [HttpGet("analytics/courts")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<List<CourtAnalyticsDto>>> GetCourtAnalytics()
    {
        var clientId = await GetClientId();
        return Ok(await _admin.GetCourtAnalyticsAsync(clientId));
    }

    [HttpGet("courts")]
    [Authorize(Roles = "admin,staff")]
    public async Task<ActionResult<List<CourtDto>>> GetCourts()
    {
        var clientId = await GetClientId();
        return Ok(await _court.GetAllCourtsAsync(clientId));
    }

    [HttpGet("courts/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<CourtDto>> GetCourt(Guid id)
    {
        var clientId = await GetClientId();
        return Ok(await _court.GetCourtByIdAsync(id, clientId));
    }

    [HttpPost("courts")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<CourtDto>> CreateCourt(CreateCourtRequest request)
    {
        var clientId = await GetClientId();
        var court = await _court.CreateCourtAsync(request, clientId);
        return CreatedAtAction(nameof(GetCourt), new { id = court.Id }, court);
    }

    [HttpPut("courts/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<CourtDto>> UpdateCourt(Guid id, UpdateCourtRequest request)
    {
        var clientId = await GetClientId();
        return Ok(await _court.UpdateCourtAsync(id, request, clientId));
    }

    [HttpDelete("courts/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteCourt(Guid id)
    {
        var clientId = await GetClientId();
        await _court.DeleteCourtAsync(id, clientId);
        return NoContent();
    }

    [HttpGet("settings")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<ClientAdminDto>> GetSettings()
    {
        // 🔒 FIX (Bug D): same tenant consistency guard as GetClientId().
        var subdomain = HttpContext.Items["Subdomain"] as string
                        ?? ClientResolver.ResolveSubdomain(HttpContext, _config);
        if (string.IsNullOrEmpty(subdomain))
            throw new UnauthorizedAccessException("Client identification required");

        var clientId = await _clientService.GetClientIdBySubdomainAsync(subdomain);
        var claimRaw = User.FindFirstValue("client_id");
        if (!Guid.TryParse(claimRaw, out var jwtClientId) || jwtClientId != clientId)
            throw new UnauthorizedAccessException("Client identification required");

        var client = await _clientService.GetClientAdminBySubdomainAsync(subdomain);
        return Ok(client);
    }

    [HttpPut("settings")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<ClientAdminDto>> UpdateSettings(UpdateClientSettingsRequest request)
    {
        var clientId = await GetClientId();
        return Ok(await _clientService.UpdateClientSettingsAsync(clientId, request));
    }

    // ========================================
    // STAFF MANAGEMENT
    // ========================================

    [HttpGet("staff")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<List<UserDto>>> GetStaff()
    {
        var clientId = await GetClientId();
        return Ok(await _admin.GetStaffByClientAsync(clientId));
    }

    [HttpPost("staff")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<UserDto>> CreateStaff(CreateStaffRequest request)
    {
        var clientId = await GetClientId();
        return Ok(await _admin.CreateStaffAsync(request, clientId));
    }

    [HttpPut("staff/{id}/status")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> UpdateStaffStatus(Guid id, UpdateStaffStatusRequest request)
    {
        var clientId = await GetClientId();
        await _admin.UpdateStaffStatusAsync(id, request.Status, clientId, ActingUserId());
        return Ok(new { message = "Staff status updated" });
    }

    [HttpDelete("staff/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> DeleteStaff(Guid id)
    {
        var clientId = await GetClientId();
        await _admin.DeleteStaffAsync(id, clientId, ActingUserId());
        return Ok(new { message = "Staff removed" });
    }

    // ========================================
    // PRICING RULES
    // ========================================

    [HttpGet("courts/{courtId}/pricing-rules")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<List<PricingRuleDto>>> GetPricingRules(Guid courtId)
    {
        var clientId = await GetClientId();
        return Ok(await _pricingRuleService.GetRulesAsync(courtId, clientId));
    }

    [HttpPost("courts/{courtId}/pricing-rules")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<PricingRuleDto>> CreatePricingRule(
        Guid courtId, CreatePricingRuleRequest request)
    {
        var clientId = await GetClientId();
        return Ok(await _pricingRuleService.CreateRuleAsync(courtId, clientId, request));
    }

    [HttpPut("pricing-rules/{ruleId}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<PricingRuleDto>> UpdatePricingRule(
        Guid ruleId, UpdatePricingRuleRequest request)
    {
        var clientId = await GetClientId();
        return Ok(await _pricingRuleService.UpdateRuleAsync(ruleId, clientId, request));
    }

    [HttpDelete("pricing-rules/{ruleId}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeletePricingRule(Guid ruleId)
    {
        var clientId = await GetClientId();
        await _pricingRuleService.DeleteRuleAsync(ruleId, clientId);
        return NoContent();
    }

    // ========================================
    // SECURITY LOGS
    // ========================================

    [HttpGet("security-events")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<List<SecurityEventDto>>> GetSecurityEvents(
        [FromQuery] string? severity,
        [FromQuery] string? eventType,
        [FromQuery] int limit = 100)
    {
        var clientId = await GetClientId();
        var q = _db.SecurityEvents
            .Where(e => e.ClientId == clientId)
            .OrderByDescending(e => e.OccurredAt)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(severity))
            q = q.Where(e => e.Severity == severity);
        if (!string.IsNullOrWhiteSpace(eventType))
            q = q.Where(e => e.EventType == eventType);

        var events = await q
            .Take(Math.Min(limit, 500))
            .Select(e => new SecurityEventDto(
                e.Id.ToString(),
                e.OccurredAt,
                e.EventType,
                e.Severity,
                e.Message,
                e.ActorEmail,
                e.IpHash,
                e.Resource,
                e.Success,
                e.ErrorCode))
            .ToListAsync();

        return Ok(events);
    }
}