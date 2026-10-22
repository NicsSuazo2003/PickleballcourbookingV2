using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;
using PickleballBookingSystem.Middleware;

namespace PickleballBookingSystem.Controllers;

[ApiController, Route("api/clients")]
public class ClientController : ControllerBase
{
    private readonly IClientService _clientService;
    private readonly IConfiguration _config;

    public ClientController(
        IClientService clientService,
        IConfiguration config)
    {
        _clientService = clientService;
        _config = config;
    }

    /// <summary>
    /// Public settings for the customer-facing site.
    /// Returns only fields required to render the landing page, checkout, and
    /// track-booking views. Never returns internal config, secrets, or the raw
    /// PaymentMethods JSON blob (it is normalized to a typed DTO).
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    [EnableRateLimiting("track")]                 // 10 req / 10 min per IP
    [ResponseCache(
        Duration = 60,
        Location = ResponseCacheLocation.Any,
        NoStore = false)]
    public async Task<ActionResult<ClientPublicDto>> GetPublicSettings()
    {
        var subdomain = HttpContext.Items["Subdomain"] as string
                        ?? ClientResolver.ResolveSubdomain(HttpContext, _config);

        if (string.IsNullOrWhiteSpace(subdomain))
            return NotFound(new { message = "Client not found" });

        try
        {
            var client = await _clientService.GetClientPublicBySubdomainAsync(subdomain);
            return Ok(client);
        }
        catch (KeyNotFoundException)
        {
            // Same 404 for "unknown tenant" and "suspended tenant" so callers
            // cannot enumerate which subdomains exist.
            return NotFound(new { message = "Client not found" });
        }
    }
}