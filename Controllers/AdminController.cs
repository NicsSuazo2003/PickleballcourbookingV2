using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;

namespace PickleballBookingSystem.Controllers;

[ApiController, Route("api/admin")]
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;
    private readonly IBookingService _booking;
    private readonly ICourtService _court;

    public AdminController(IAdminService admin, IBookingService booking, ICourtService court)
    {
        _admin = admin;
        _booking = booking;
        _court = court;
    }

    [HttpGet("analytics")]
    public async Task<ActionResult<AnalyticsDto>> GetAnalytics()
    {
        var analytics = await _admin.GetAnalyticsAsync();
        return Ok(analytics);
    }

    // ✅ NEW - Per-court analytics
    [HttpGet("analytics/courts")]
    public async Task<ActionResult<List<CourtAnalyticsDto>>> GetCourtAnalytics()
    {
        var analytics = await _admin.GetCourtAnalyticsAsync();
        return Ok(analytics);
    }

    [HttpGet("bookings")]
    public async Task<ActionResult<List<BookingDto>>> GetBookings()
    {
        var bookings = await _booking.GetAllBookingsAsync();
        return Ok(bookings);
    }

    [HttpPut("bookings/{id}")]
    public async Task<ActionResult<BookingDto>> UpdateBooking(Guid id, AdminUpdateBookingRequest request)
    {
        var booking = await _booking.AdminUpdateBookingAsync(id, request);
        return Ok(booking);
    }

    // ✅ NEW - Get all courts (admin)
    [HttpGet("courts")]
    public async Task<ActionResult<List<CourtDto>>> GetCourts()
    {
        var courts = await _court.GetAllCourtsAsync();
        return Ok(courts);
    }

    // ✅ NEW - Get single court (admin)
    [HttpGet("courts/{id}")]
    public async Task<ActionResult<CourtDto>> GetCourt(Guid id)
    {
        var court = await _court.GetCourtByIdAsync(id);
        return Ok(court);
    }

    // ✅ NEW - Create court
    [HttpPost("courts")]
    public async Task<ActionResult<CourtDto>> CreateCourt(CreateCourtRequest request)
    {
        var court = await _court.CreateCourtAsync(request);
        return CreatedAtAction(nameof(GetCourt), new { id = court.Id }, court);
    }

    // ✅ NEW - Update court
    [HttpPut("courts/{id}")]
    public async Task<ActionResult<CourtDto>> UpdateCourt(Guid id, UpdateCourtRequest request)
    {
        var court = await _court.UpdateCourtAsync(id, request);
        return Ok(court);
    }

    // ✅ NEW - Delete court
    [HttpDelete("courts/{id}")]
    public async Task<IActionResult> DeleteCourt(Guid id)
    {
        await _court.DeleteCourtAsync(id);
        return NoContent();
    }
}