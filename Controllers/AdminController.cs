using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;
using PickleballBookingSystem.Middleware;

namespace PickleballBookingSystem.Controllers;

[ApiController, Route("api/admin")]
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;
    private readonly IBookingService _booking;
    private readonly ICourtService _court;
    private readonly ClientResolver _clientResolver;
    private readonly IClientService _clientService;

    public AdminController(
        IAdminService admin,
        IBookingService booking,
        ICourtService court,
        ClientResolver clientResolver,
        IClientService clientService)
    {
        _admin = admin;
        _booking = booking;
        _court = court;
        _clientResolver = clientResolver;
        _clientService = clientService;
    }

    private async Task<Guid> GetClientId()
    {
        var subdomain = _clientResolver.GetSubdomain();
        if (string.IsNullOrEmpty(subdomain))
            throw new UnauthorizedAccessException("Client identification required");

        return await _clientService.GetClientIdBySubdomainAsync(subdomain);
    }

    [HttpGet("analytics")]
    public async Task<ActionResult<AnalyticsDto>> GetAnalytics()
    {
        var clientId = await GetClientId();
        var analytics = await _admin.GetAnalyticsAsync(clientId);
        return Ok(analytics);
    }

    [HttpGet("analytics/courts")]
    public async Task<ActionResult<List<CourtAnalyticsDto>>> GetCourtAnalytics()
    {
        var clientId = await GetClientId();
        var analytics = await _admin.GetCourtAnalyticsAsync(clientId);
        return Ok(analytics);
    }

    [HttpGet("bookings")]
    public async Task<ActionResult<List<BookingDto>>> GetBookings()
    {
        var clientId = await GetClientId();
        var bookings = await _booking.GetAllBookingsAsync(clientId);
        return Ok(bookings);
    }

    [HttpPut("bookings/{id}")]
    public async Task<ActionResult<BookingDto>> UpdateBooking(Guid id, AdminUpdateBookingRequest request)
    {
        var clientId = await GetClientId();
        var booking = await _booking.AdminUpdateBookingAsync(id, request, clientId);
        return Ok(booking);
    }

    [HttpGet("courts")]
    public async Task<ActionResult<List<CourtDto>>> GetCourts()
    {
        var clientId = await GetClientId();
        var courts = await _court.GetAllCourtsAsync(clientId);
        return Ok(courts);
    }

    [HttpGet("courts/{id}")]
    public async Task<ActionResult<CourtDto>> GetCourt(Guid id)
    {
        var clientId = await GetClientId();
        var court = await _court.GetCourtByIdAsync(id, clientId);
        return Ok(court);
    }

    [HttpPost("courts")]
    public async Task<ActionResult<CourtDto>> CreateCourt(CreateCourtRequest request)
    {
        var clientId = await GetClientId();
        var court = await _court.CreateCourtAsync(request, clientId);
        return CreatedAtAction(nameof(GetCourt), new { id = court.Id }, court);
    }

    [HttpPut("courts/{id}")]
    public async Task<ActionResult<CourtDto>> UpdateCourt(Guid id, UpdateCourtRequest request)
    {
        var clientId = await GetClientId();
        var court = await _court.UpdateCourtAsync(id, request, clientId);
        return Ok(court);
    }

    [HttpDelete("courts/{id}")]
    public async Task<IActionResult> DeleteCourt(Guid id)
    {
        var clientId = await GetClientId();
        await _court.DeleteCourtAsync(id, clientId);
        return NoContent();
    }
    [HttpGet("debug-headers")]
    [AllowAnonymous]
    public IActionResult DebugHeaders()
    {
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
        return Ok(headers)
}