using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using PickleballBookingSystem.Interfaces;

namespace PickleballBookingSystem.Middleware;

/// <summary>
/// Resolves the tenant subdomain from the X-Client-Subdomain header or host,
/// strictly validated against an allowlist configured in appsettings.json:
///   "Clients": { "AllowedSubdomains": ["picklejoe", "sideout", ...] }
///
/// Runs as middleware. On each request:
///   1. Resolves the subdomain
///   2. Looks up the ClientId
///   3. Stores it in HttpContext.Items["ClientId"] so downstream code
///      (controllers, OnRejected handler, ExceptionMiddleware) can read it
///      without re-querying.
/// </summary>
public class ClientResolver
{
    private readonly RequestDelegate _next;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HashSet<string> _allowed;
    private readonly string? _defaultSubdomain;

    public ClientResolver(
        RequestDelegate next,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration config)
    {
        _next = next;
        _httpContextAccessor = httpContextAccessor;

        var list = config.GetSection("Clients:AllowedSubdomains").Get<string[]>()
                   ?? Array.Empty<string>();
        _allowed = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);

        _defaultSubdomain = config["Clients:DefaultSubdomain"];
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var subdomain = ResolveSubdomain(ctx);

        if (!string.IsNullOrEmpty(subdomain))
        {
            try
            {
                // Resolve IClientService via RequestServices — middleware runs
                // before the DI scope is guaranteed to be set up, so resolving
                // via constructor is unsafe for scoped services.
                var clientService = ctx.RequestServices.GetService<IClientService>();
                if (clientService is not null)
                {
                    var clientId = await clientService.GetClientIdBySubdomainAsync(subdomain);
                    ctx.Items["ClientId"] = clientId;
                    ctx.Items["Subdomain"] = subdomain;
                }
            }
            catch
            {
                // Don't fail the whole request if the client lookup fails —
                // the controller will produce a 401 if it actually needs
                // the client ID and it's missing.
            }
        }

        await _next(ctx);
    }

    /// <summary>
    /// Returns the validated subdomain for the current request, or null.
    /// Controllers can still call this directly (same behaviour as before).
    /// </summary>
    public string? GetSubdomain()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null) return null;

        // If middleware already resolved it, reuse it
        if (ctx.Items["Subdomain"] is string cached && !string.IsNullOrEmpty(cached))
            return cached;

        return ResolveSubdomain(ctx);
    }

    // ── private helpers ─────────────────────────────────────

    private string? ResolveSubdomain(HttpContext ctx)
    {
        // 1. Explicit header (frontend sends it)
        var fromHeader = ctx.Request.Headers["X-Client-Subdomain"].FirstOrDefault();

        // 2. Fallback to host
        var candidate = fromHeader;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            var host = ctx.Request.Host.Host;
            if (!string.IsNullOrEmpty(host))
            {
                var parts = host.Split('.');
                if (parts.Length >= 2 && parts[0] != "www" && parts[0] != "localhost")
                    candidate = parts[0];
            }
        }

        // 3. Fallback to configured default (dev / single-tenant prod)
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = _defaultSubdomain;

        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        candidate = candidate.Trim().ToLowerInvariant();

        // 4. Reject anything not on the allowlist
        return _allowed.Contains(candidate) ? candidate : null;
    }
}