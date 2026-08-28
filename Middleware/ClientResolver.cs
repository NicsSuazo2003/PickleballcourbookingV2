namespace PickleballBookingSystem.Middleware;

public class ClientResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClientResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetSubdomain()
    {
        var host = _httpContextAccessor.HttpContext?.Request.Host.Host;
        if (string.IsNullOrEmpty(host)) return null;

        var parts = host.Split('.');
        if (parts.Length >= 2 && parts[0] != "www" && parts[0] != "localhost")
        {
            return parts[0];
        }

        // Development fallback - use header
        return _httpContextAccessor.HttpContext?.Request.Headers["X-Client-Subdomain"].FirstOrDefault();
    }
}