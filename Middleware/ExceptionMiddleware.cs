using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PickleballBookingSystem.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            var (status, message) = ex switch
            {
                UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized."),
                KeyNotFoundException => (HttpStatusCode.NotFound, "Not found."),
                ArgumentException => (HttpStatusCode.BadRequest, SafeMessage(ex.Message)),
                InvalidOperationException => (HttpStatusCode.Conflict, SafeMessage(ex.Message)),
                DbUpdateException => (HttpStatusCode.Conflict, "This record was modified. Please refresh and try again."),
                _ => (HttpStatusCode.InternalServerError, "Something went wrong. Please try again."),
            };

            if (context.Response.HasStarted) return;

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)status;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message }));
        }
    }

    // Prevent Npgsql/EF internals from leaking through
    private static string SafeMessage(string msg) =>
        msg.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
        msg.Contains("relation", StringComparison.OrdinalIgnoreCase) ||
        msg.Contains("column", StringComparison.OrdinalIgnoreCase) ||
        msg.Contains("constraint", StringComparison.OrdinalIgnoreCase)
            ? "The request could not be completed."
            : msg;
}