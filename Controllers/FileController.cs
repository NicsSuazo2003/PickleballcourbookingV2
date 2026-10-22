using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;
using PickleballBookingSystem.Middleware;

namespace PickleballBookingSystem.Controllers;

[ApiController, Route("api/files")]
[Authorize(Roles = "admin")]
public class FileController : ControllerBase
{
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const string Bucket = "PickleImgs";

    private static readonly (string Mime, string Ext, byte[] Magic)[] AllowedImages = new[]
    {
        ("image/jpeg", ".jpg",  new byte[] { 0xFF, 0xD8, 0xFF }),
        ("image/png",  ".png",  new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        ("image/webp", ".webp", new byte[] { 0x52, 0x49, 0x46, 0x46 }),
        ("image/gif",  ".gif",  new byte[] { 0x47, 0x49, 0x46, 0x38 }),
    };

    private static readonly Regex SafeFileNameRegex =
        new(@"^[0-9a-f]{32}\.(?:jpg|png|webp|gif)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IClientService _clientService;

    public FileController(
        IConfiguration config,
        IHttpClientFactory httpFactory,
        IClientService clientService)
    {
        _config = config;
        _httpFactory = httpFactory;
        _clientService = clientService;
    }

    private async Task<Guid> GetClientIdAsync()
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
            throw new UnauthorizedAccessException("Client not found");
        }
    }

    // ═══════════════════════════════════════════════════════
    // UPLOAD
    // ═══════════════════════════════════════════════════════
    [HttpPost("upload")]
    [EnableRateLimiting("upload")]
    [RequestSizeLimit(MaxFileBytes + 1024)]
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        if (file.Length > MaxFileBytes)
            return BadRequest(new { message = "File must be under 5 MB" });

        using var stream = file.OpenReadStream();
        var header = new byte[8];
        var read = await stream.ReadAsync(header.AsMemory(0, 8));
        if (read < 4)
            return BadRequest(new { message = "File is not a valid image" });

        var matched = Array.Find(AllowedImages, a =>
            a.Magic.Length <= read &&
            header.AsSpan(0, a.Magic.Length).SequenceEqual(a.Magic));

        if (matched.Mime is null)
            return BadRequest(new { message = "Only JPEG, PNG, WebP, and GIF are allowed" });

        stream.Position = 0;

        var clientId = await GetClientIdAsync();

        var fileName = $"{Guid.NewGuid():N}{matched.Ext}";
        var objectPath = $"{clientId}/{fileName}";

        var supabaseUrl = _config["Supabase:Url"]
            ?? throw new InvalidOperationException("Supabase:Url not configured");
        var supabaseKey = _config["Supabase:Key"]
            ?? throw new InvalidOperationException("Supabase:Key not configured");

        var http = _httpFactory.CreateClient("supabase");

        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(matched.Mime);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{supabaseUrl}/storage/v1/object/{Bucket}/{objectPath}")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supabaseKey);
        request.Headers.Add("x-upsert", "false");

        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return BadRequest(new { message = "Upload failed" });

        var url = $"{supabaseUrl}/storage/v1/object/public/{Bucket}/{objectPath}";
        return Ok(new { url });
    }

    // ═══════════════════════════════════════════════════════
    // DELETE
    // ═══════════════════════════════════════════════════════
    [HttpDelete]
    [EnableRateLimiting("upload")]
    public async Task<IActionResult> Delete([FromBody] DeleteFileRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new { message = "Url is required" });

        var supabaseUrl = _config["Supabase:Url"]
            ?? throw new InvalidOperationException("Supabase:Url not configured");
        var supabaseKey = _config["Supabase:Key"]
            ?? throw new InvalidOperationException("Supabase:Key not configured");

        Uri parsed;
        try { parsed = new Uri(request.Url); }
        catch { return BadRequest(new { message = "Invalid URL" }); }

        var expectedPrefix =
            $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/public/{Bucket}/";
        if (!request.Url.StartsWith(expectedPrefix, StringComparison.Ordinal))
            return BadRequest(new { message = "URL does not belong to this bucket" });

        var relative = request.Url[expectedPrefix.Length..];
        var slashIdx = relative.IndexOf('/');
        if (slashIdx <= 0)
            return BadRequest(new { message = "Invalid file path" });

        var pathClientIdStr = relative[..slashIdx];
        var fileName = relative[(slashIdx + 1)..];

        if (!Guid.TryParse(pathClientIdStr, out var pathClientId))
            return BadRequest(new { message = "Invalid file path" });

        if (!SafeFileNameRegex.IsMatch(fileName))
            return BadRequest(new { message = "Invalid file name" });

        var callerClientId = await GetClientIdAsync();
        if (callerClientId != pathClientId)
            return Forbid();

        var objectPath = $"{pathClientIdStr}/{fileName}";
        var http = _httpFactory.CreateClient("supabase");

        using var requestDel = new HttpRequestMessage(HttpMethod.Delete,
            $"{supabaseUrl}/storage/v1/object/{Bucket}/{objectPath}");
        requestDel.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supabaseKey);

        using var resp = await http.SendAsync(requestDel);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return NotFound(new { message = "File not found" });

        if (!resp.IsSuccessStatusCode)
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Failed to delete file" });

        return NoContent();
    }
}