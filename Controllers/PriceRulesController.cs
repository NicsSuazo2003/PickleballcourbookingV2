using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;
using PickleballBookingSystem.Middleware;

namespace PickleballBookingSystem.Controllers;

[ApiController, Route("api/price-rules")]
public class PriceRulesController : ControllerBase
{
    private readonly ICourtService _courtService;
    private readonly IClientService _clientService;
    private readonly IConfiguration _config;

    public PriceRulesController(
        ICourtService courtService,
        IClientService clientService,
        IConfiguration config)
    {
        _courtService = courtService;
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

    [HttpGet]
    public async Task<ActionResult<List<PriceRuleDto>>> GetAll()
    {
        var clientId = await GetClientId();
        return Ok(await _courtService.GetPriceRulesAsync(clientId));
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<ActionResult<PriceRuleDto>> Create(CreatePriceRuleRequest request)
    {
        var clientId = await GetClientId();
        var rule = await _courtService.CreatePriceRuleAsync(request, clientId);
        return CreatedAtAction(nameof(GetAll), rule);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id}")]
    public async Task<ActionResult<PriceRuleDto>> Update(Guid id, UpdatePriceRuleRequest request)
    {
        var clientId = await GetClientId();
        var rule = await _courtService.UpdatePriceRuleAsync(id, request, clientId);
        return Ok(rule);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var clientId = await GetClientId();
        await _courtService.DeletePriceRuleAsync(id, clientId);
        return NoContent();
    }
}