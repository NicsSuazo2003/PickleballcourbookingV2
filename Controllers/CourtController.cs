using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;
using PickleballBookingSystem.Middleware;

namespace PickleballBookingSystem.Controllers;

[ApiController]
[Route("api/courts")]
public class CourtController : ControllerBase
{
    private readonly ICourtService _court;
    private readonly IClientService _clientService;
    private readonly IConfiguration _config;

    public CourtController(
        ICourtService court,
        IClientService clientService,
        IConfiguration config)
    {
        _court = court;
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

    // ========================================
    // PUBLIC ENDPOINTS
    // ========================================

    [HttpGet]
    public async Task<ActionResult<List<CourtDto>>> GetAllCourts()
    {
        var clientId = await GetClientId();
        return Ok(await _court.GetAllCourtsAsync(clientId));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CourtDto>> GetCourt(Guid id)
    {
        var clientId = await GetClientId();
        return Ok(await _court.GetCourtByIdAsync(id, clientId));
    }

    [HttpGet("{id}/availability")]
    public async Task<ActionResult<List<TimeSlotAvailabilityDto>>> GetAvailability(
        Guid id,
        [FromQuery] DateTime date,
        [FromQuery] Guid? excludeBookingId = null)
    {
        var clientId = await GetClientId();
        return Ok(await _court.GetCourtAvailabilityAsync(id, date, clientId, excludeBookingId));
    }

    [HttpGet("{id}/blocked-dates")]
    public async Task<ActionResult<List<BlockedDateDto>>> GetBlockedDates(Guid id)
    {
        var clientId = await GetClientId();
        return Ok(await _court.GetBlockedDatesAsync(id, clientId));
    }

    // ========================================
    // ADMIN ENDPOINTS
    // ========================================

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<ActionResult<CourtDto>> CreateCourt(CreateCourtRequest request)
    {
        var clientId = await GetClientId();
        var court = await _court.CreateCourtAsync(request, clientId);
        return CreatedAtAction(nameof(GetCourt), new { id = court.Id }, court);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id}")]
    public async Task<ActionResult<CourtDto>> UpdateCourt(Guid id, UpdateCourtRequest request)
    {
        var clientId = await GetClientId();
        return Ok(await _court.UpdateCourtAsync(id, request, clientId));
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCourt(Guid id)
    {
        var clientId = await GetClientId();
        await _court.DeleteCourtAsync(id, clientId);
        return NoContent();
    }

    [Authorize(Roles = "admin")]
    [HttpPost("{id}/blocked-dates")]
    public async Task<ActionResult<BlockedDateDto>> AddBlockedDate(Guid id, CreateBlockedDateRequest request)
    {
        var clientId = await GetClientId();
        var result = await _court.AddBlockedDateAsync(request, id, clientId);
        return Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("blocked-dates/{blockedId}")]
    public async Task<IActionResult> DeleteBlockedDate(Guid blockedId)
    {
        var clientId = await GetClientId();
        await _court.DeleteBlockedDateAsync(blockedId, clientId);
        return NoContent();
    }

    [Authorize(Roles = "admin")]
    [HttpGet("blocked-dates")]
    public async Task<ActionResult<List<BlockedDateDto>>> GetBlockedDatesAll()
    {
        var clientId = await GetClientId();
        return Ok(await _court.GetBlockedDatesAsync(null, clientId));
    }

    [Authorize(Roles = "admin")]
    [HttpPost("blocked-dates")]
    public async Task<ActionResult<BlockedDateDto>> AddBlockedDateAll(CreateBlockedDateRequest request)
    {
        var clientId = await GetClientId();
        var result = await _court.AddBlockedDateAsync(request, null, clientId);
        return Ok(result);
    }

    // ========================================
    // PRICE RULES ENDPOINTS
    // ========================================

    [HttpGet("price-rules")]
    public async Task<ActionResult<List<PriceRuleDto>>> GetPriceRules()
    {
        var clientId = await GetClientId();
        return Ok(await _court.GetPriceRulesAsync(clientId));
    }

    [Authorize(Roles = "admin")]
    [HttpPost("price-rules")]
    public async Task<ActionResult<PriceRuleDto>> CreatePriceRule(CreatePriceRuleRequest request)
    {
        var clientId = await GetClientId();
        var rule = await _court.CreatePriceRuleAsync(request, clientId);
        return CreatedAtAction(nameof(GetPriceRules), new { }, rule);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("price-rules/{id}")]
    public async Task<ActionResult<PriceRuleDto>> UpdatePriceRule(Guid id, UpdatePriceRuleRequest request)
    {
        var clientId = await GetClientId();
        return Ok(await _court.UpdatePriceRuleAsync(id, request, clientId));
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("price-rules/{id}")]
    public async Task<IActionResult> DeletePriceRule(Guid id)
    {
        var clientId = await GetClientId();
        await _court.DeletePriceRuleAsync(id, clientId);
        return NoContent();
    }
}