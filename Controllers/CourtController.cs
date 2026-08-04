using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;

namespace PickleballBookingSystem.Controllers;

[ApiController]
[Route("api/courts")]  // ✅ Changed from "api/court" to "api/courts"
public class CourtController : ControllerBase
{
    private readonly ICourtService _court;

    public CourtController(ICourtService court) => _court = court;

    // ========================================
    // ✅ PUBLIC ENDPOINTS
    // ========================================

    // GET all courts
    [HttpGet]
    public async Task<ActionResult<List<CourtDto>>> GetAllCourts()
    {
        var courts = await _court.GetAllCourtsAsync();
        return Ok(courts);
    }

    // GET single court
    [HttpGet("{id}")]
    public async Task<ActionResult<CourtDto>> GetCourt(Guid id)
    {
        var court = await _court.GetCourtByIdAsync(id);
        return Ok(court);
    }

    // GET court availability
    [HttpGet("{id}/availability")]
    public async Task<ActionResult<List<TimeSlotAvailabilityDto>>> GetAvailability(Guid id, [FromQuery] DateTime date)
    {
        var slots = await _court.GetCourtAvailabilityAsync(id, date);
        return Ok(slots);
    }

    // GET blocked dates for a specific court
    [HttpGet("{id}/blocked-dates")]
    public async Task<ActionResult<List<BlockedDateDto>>> GetBlockedDates(Guid id)
    {
        var blockedDates = await _court.GetBlockedDatesAsync(id);
        return Ok(blockedDates);
    }

    // ========================================
    // ✅ ADMIN ENDPOINTS
    // ========================================

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<ActionResult<CourtDto>> CreateCourt(CreateCourtRequest request)
    {
        var court = await _court.CreateCourtAsync(request);
        return CreatedAtAction(nameof(GetCourt), new { id = court.Id }, court);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id}")]
    public async Task<ActionResult<CourtDto>> UpdateCourt(Guid id, UpdateCourtRequest request)
    {
        var court = await _court.UpdateCourtAsync(id, request);
        return Ok(court);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCourt(Guid id)
    {
        await _court.DeleteCourtAsync(id);
        return NoContent();
    }

    [Authorize(Roles = "admin")]
    [HttpPost("{id}/blocked-dates")]
    public async Task<ActionResult<BlockedDateDto>> AddBlockedDate(Guid id, CreateBlockedDateRequest request)
    {
        var result = await _court.AddBlockedDateAsync(request, id);
        return CreatedAtAction(nameof(GetBlockedDates), new { id }, result);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("blocked-dates/{blockedId}")]
    public async Task<IActionResult> DeleteBlockedDate(Guid blockedId)
    {
        await _court.DeleteBlockedDateAsync(blockedId);
        return NoContent();
    }

    // ========================================
    // ⚠️ DEPRECATED - Single Court Endpoints (Backward Compatibility)
    // ========================================

    [HttpGet("legacy")]
    public async Task<ActionResult<CourtDto>> GetCourtLegacy()
    {
        var court = await _court.GetCourtAsync();
        return Ok(court);
    }

    [HttpGet("legacy/availability")]
    public async Task<ActionResult<List<TimeSlotAvailabilityDto>>> GetAvailabilityLegacy([FromQuery] DateTime date)
    {
        var slots = await _court.GetAvailabilityAsync(date);
        return Ok(slots);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("legacy/settings")]
    public async Task<ActionResult<CourtDto>> UpdateSettingsLegacy(UpdateCourtRequest request)
    {
        var court = await _court.UpdateCourtSettingsAsync(request);
        return Ok(court);
    }

    [Authorize(Roles = "admin")]
    [HttpGet("legacy/blocked-dates")]
    public async Task<ActionResult<List<BlockedDateDto>>> GetBlockedDatesLegacy()
    {
        return Ok(await _court.GetBlockedDatesAsync());
    }

    [Authorize(Roles = "admin")]
    [HttpPost("legacy/blocked-dates")]
    public async Task<ActionResult<BlockedDateDto>> AddBlockedDateLegacy(CreateBlockedDateRequest request)
    {
        var result = await _court.AddBlockedDateAsync(request);
        return CreatedAtAction(nameof(GetBlockedDatesLegacy), result);
    }

    // ========================================
    // ✅ PRICE RULES ENDPOINTS
    // ========================================

    [HttpGet("price-rules")]
    public async Task<ActionResult<List<PriceRuleDto>>> GetPriceRules()
    {
        var rules = await _court.GetPriceRulesAsync();
        return Ok(rules);
    }

    [Authorize(Roles = "admin")]
    [HttpPost("price-rules")]
    public async Task<ActionResult<PriceRuleDto>> CreatePriceRule(CreatePriceRuleRequest request)
    {
        var rule = await _court.CreatePriceRuleAsync(request);
        return CreatedAtAction(nameof(GetPriceRules), rule);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("price-rules/{id}")]
    public async Task<ActionResult<PriceRuleDto>> UpdatePriceRule(Guid id, UpdatePriceRuleRequest request)
    {
        var rule = await _court.UpdatePriceRuleAsync(id, request);
        return Ok(rule);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("price-rules/{id}")]
    public async Task<IActionResult> DeletePriceRule(Guid id)
    {
        await _court.DeletePriceRuleAsync(id);
        return NoContent();
    }
}