using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using PickleballBookingSystem.Interfaces;

namespace PickleballBookingSystem.Middleware;

/// <summary>
/// Resolves the tenant subdomain from the X-Client-Subdomain header or host,
/// strictly validated against an allowlist configured in appsettings.json.
///
/// Runs as middleware ONLY. On each request it resolves ClientId and stores
/// it in HttpContext.Items["ClientId"] and HttpContext.Items["Subdomain"].
/// Controllers and middleware can read these directly.
/// </summary>
public class ClientResolver
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _allowed;
    private readonly string? _defaultSubdomain;

    private static readonly Dictionary<string, Guid> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ClientResolver(RequestDelegate next, IConfiguration config)
    {
        _next = next;

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
                if (_cache.TryGetValue(subdomain, out var cachedClientId))
                {
                    ctx.Items["ClientId"] = cachedClientId;
                    ctx.Items["Subdomain"] = subdomain;
                }
                else
                {
                    var clientService = ctx.RequestServices.GetService<IClientService>();
                    if (clientService is not null)
                    {
                        var clientId = await clientService.GetClientIdBySubdomainAsync(subdomain);
                        _cache[subdomain] = clientId;
                        ctx.Items["ClientId"] = clientId;
                        ctx.Items["Subdomain"] = subdomain;
                    }
                }
            }
            catch
            {
                // Swallow — controllers that need ClientId will 401 if missing.
            }
        }

        await _next(ctx);
    }

    // ── static helper — controllers can call this WITHOUT needing ClientResolver injected ──
    public static string? ResolveSubdomain(HttpContext ctx, IConfiguration config)
    {
        var allowed = config.GetSection("Clients:AllowedSubdomains").Get<string[]>()
                      ?? Array.Empty<string>();
        var defaultSub = config["Clients:DefaultSubdomain"];

        var fromHeader = ctx.Request.Headers["X-Client-Subdomain"].FirstOrDefault();

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

        if (string.IsNullOrWhiteSpace(candidate))
            candidate = defaultSub;

        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        candidate = candidate.Trim().ToLowerInvariant();

        var allowedSet = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        return allowedSet.Contains(candidate) ? candidate : null;
    }

    private string? ResolveSubdomain(HttpContext ctx)
    {
        // Prefer the cached subdomain from middleware
        if (ctx.Items["Subdomain"] is string cached && !string.IsNullOrEmpty(cached))
            return cached;

        // Same logic as static method — kept as private instance for use inside InvokeAsync
        var fromHeader = ctx.Request.Headers["X-Client-Subdomain"].FirstOrDefault();

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

        if (string.IsNullOrWhiteSpace(candidate))
            candidate = _defaultSubdomain;

        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        candidate = candidate.Trim().ToLowerInvariant();

        return _allowed.Contains(candidate) ? candidate : null;
    }
}