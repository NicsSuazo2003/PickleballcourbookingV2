using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;
using PickleballBookingSystem.Middleware;

namespace PickleballBookingSystem.Controllers;

[ApiController, Route("api/bookings")]
public class BookingController : ControllerBase
{
    private const long MaxScreenshotBytes = 5 * 1024 * 1024;

    private static readonly (string Mime, byte[] Magic)[] AllowedImageSignatures = new[]
    {
        ("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF }),
        ("image/png",  new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        ("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46 }),
    };

    private readonly IBookingService _booking;
    private readonly IConfiguration _config;
    private readonly IClientService _clientService;
    private readonly IHttpClientFactory _httpFactory;

    public BookingController(
        IBookingService booking,
        IConfiguration config,
        IClientService clientService,
        IHttpClientFactory httpFactory)
    {
        _booking = booking;
        _config = config;
        _clientService = clientService;
        _httpFactory = httpFactory;
    }

    private async Task<Guid> GetClientId()
    {
        var subdomain = HttpContext.Items["Subdomain"] as string
                        ?? ClientResolver.ResolveSubdomain(HttpContext, _config);
        if (string.IsNullOrEmpty(subdomain))
            throw new UnauthorizedAccessException("Client identification required");

        try
        {
            return await _clientService.GetClientIdBySubdomainAsync(subdomain);
        }
        catch (KeyNotFoundException)
        {
            throw new UnauthorizedAccessException("Client not found for this request.");
        }
    }

    [HttpPost]
    [EnableRateLimiting("booking")]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingRequest request)
    {
        var clientId = await GetClientId();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = HttpContext.Request.Headers["User-Agent"].FirstOrDefault();
        var booking = await _booking.CreateBookingAsync(request, clientId, ip, ua);
        return CreatedAtAction(nameof(Track), new { referenceCode = booking.ReferenceCode }, booking);
    }

    [HttpGet("track/{referenceCode}")]
    [EnableRateLimiting("track")]
    public async Task<ActionResult<BookingDto>> Track(string referenceCode, [FromQuery] string? email)
    {
        var clientId = await GetClientId();
        var booking = await _booking.TrackBookingAsync(referenceCode, email, clientId);
        return Ok(booking);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BookingDto>> GetById(Guid id)
    {
        var clientId = await GetClientId();
        var booking = await _booking.GetBookingAsync(id, clientId);
        return Ok(booking);
    }

    [HttpGet("track-summaries-by-email")]
    [EnableRateLimiting("track")]
    public async Task<ActionResult<List<BookingSummaryDto>>> TrackSummariesByEmail([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "Email is required" });

        var clientId = await GetClientId();
        var summaries = await _booking.TrackBookingSummariesByEmailAsync(email, clientId);
        return Ok(summaries);
    }

    [HttpPost("{id}/upload-payment")]
    [EnableRateLimiting("upload")]
    [RequestSizeLimit(MaxScreenshotBytes + 1024)]
    public async Task<ActionResult<BookingDto>> UploadPayment(
        Guid id,
        [FromForm] string paymentReference,
        [FromForm] string? paymentMethod,
        IFormFile? screenshot)
    {
        if (string.IsNullOrWhiteSpace(paymentReference))
            return BadRequest(new { message = "Payment reference is required" });
        if (paymentReference.Trim().Length > 64)
            return BadRequest(new { message = "Payment reference is too long" });

        var clientId = await GetClientId();
        string? screenshotUrl = null;

        if (screenshot is { Length: > 0 })
        {
            if (screenshot.Length > MaxScreenshotBytes)
                return BadRequest(new { message = "Screenshot must be under 5 MB." });

            using var stream = screenshot.OpenReadStream();
            var header = new byte[4];
            var read = await stream.ReadAsync(header.AsMemory(0, 4));
            if (read < 4)
                return BadRequest(new { message = "Screenshot is not a valid image." });

            var matched = Array.Find(AllowedImageSignatures, a =>
                a.Magic.Length <= read &&
                header.AsSpan(0, a.Magic.Length).SequenceEqual(a.Magic));

            if (matched.Mime is null)
                return BadRequest(new { message = "Only JPG, PNG, or WEBP screenshots are accepted." });

            stream.Position = 0;

            var supabaseUrl = _config["Supabase:Url"]
                ?? throw new InvalidOperationException("Supabase:Url not configured");
            var supabaseKey = _config["Supabase:Key"]
                ?? throw new InvalidOperationException("Supabase:Key not configured");

            var ext = matched.Mime switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".jpg",
            };
            var fileName = $"payment-{Guid.NewGuid():N}{ext}";

            var client = _httpFactory.CreateClient("supabase");
            using var content = new StreamContent(stream);
            content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(matched.Mime);

            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{supabaseUrl}/storage/v1/object/PickleImgs/{fileName}")
            {
                Content = content
            };
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", supabaseKey);

            using var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return BadRequest(new { message = "Screenshot upload failed" });

            screenshotUrl = $"{supabaseUrl}/storage/v1/object/public/PickleImgs/{fileName}";
        }

        var booking = await _booking.UploadPaymentScreenshotAsync(
            id,
            screenshotUrl,
            paymentReference.Trim(),
            paymentMethod,
            clientId);

        return Ok(booking);
    }

    [Authorize(Roles = "admin,staff"), HttpGet("admin")]
    public async Task<ActionResult<List<BookingDto>>> GetAll([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var clientId = await GetClientId();
        var bookings = await _booking.GetBookingsAsync(clientId, from, to);
        return Ok(bookings);
    }

    [Authorize(Roles = "admin"), HttpPut("admin/{id}")]
    public async Task<ActionResult<BookingDto>> AdminUpdate(Guid id, AdminUpdateBookingRequest request)
    {
        var clientId = await GetClientId();
        var booking = await _booking.AdminUpdateBookingAsync(id, request, clientId);
        return Ok(booking);
    }

    [Authorize(Roles = "admin"), HttpPost("admin-create")]
    public async Task<ActionResult<BookingDto>> AdminCreate(CreateBookingRequest request)
    {
        var clientId = await GetClientId();
        request = request with { AdminOverride = true };
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = HttpContext.Request.Headers["User-Agent"].FirstOrDefault();
        var booking = await _booking.CreateBookingAsync(request, clientId, ip, ua);
        return Ok(booking);
    }
}