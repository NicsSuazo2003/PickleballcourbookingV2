using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;

namespace PickleballBookingSystem.Middleware;

/// <summary>
/// Resolves the tenant subdomain from the X-Client-Subdomain header or host,
/// strictly validated against an allowlist configured in appsettings.json:
///   "Clients": { "AllowedSubdomains": ["picklejoe", "sideout", ...] }
/// </summary>
public class ClientResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HashSet<string> _allowed;
    private readonly string? _defaultSubdomain;

    public ClientResolver(IHttpContextAccessor httpContextAccessor, IConfiguration config)
    {
        _httpContextAccessor = httpContextAccessor;

        var list = config.GetSection("Clients:AllowedSubdomains").Get<string[]>() ?? Array.Empty<string>();
        _allowed = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);

        _defaultSubdomain = config["Clients:DefaultSubdomain"];
    }

    public string? GetSubdomain()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null) return null;

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

        // 3. Fallback to configured default (only useful in dev / single-tenant prod)
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = _defaultSubdomain;

        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        candidate = candidate.Trim().ToLowerInvariant();

        // 4. Reject anything not on the allowlist
        return _allowed.Contains(candidate) ? candidate : null;
    }
}