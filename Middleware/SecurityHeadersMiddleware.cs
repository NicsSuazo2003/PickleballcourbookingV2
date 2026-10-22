using Microsoft.AspNetCore.Http;

namespace PickleballBookingSystem.Middleware;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;

        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Cross-Origin-Resource-Policy"] = "same-site";
        h["X-Permitted-Cross-Domain-Policies"] = "none";

        if (ctx.Request.IsHttps)
            h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        // Only apply CSP to HTML responses to avoid breaking API JSON
        var csp =
            "default-src 'self'; " +
            "img-src 'self' data: https:; " +
            "font-src 'self' data:; " +
            "script-src 'self' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "connect-src 'self' https:; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self';";
        h["Content-Security-Policy"] = csp;

        await _next(ctx);
    }
}