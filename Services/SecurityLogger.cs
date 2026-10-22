using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.Entities;

namespace PickleballBookingSystem.Services;

public interface ISecurityLogger
{
    Task LogAsync(SecurityEvent evt);
    string? HashIp(string? ip);
    string? HashEmail(string? email);
}

public sealed class SecurityEvent
{
    public Guid ClientId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ActorEmail { get; init; }
    public string? IpHash { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public required string EventType { get; init; }
    public required string Severity { get; init; }
    public string? Resource { get; init; }
    public required string Message { get; init; }
    public object? Metadata { get; init; }
    public bool Success { get; init; } = true;
    public string? ErrorCode { get; init; }
}

public sealed class SecurityLogger : ISecurityLogger
{
    private readonly AppDbContext _db;
    private readonly ILogger<SecurityLogger> _fallback;

    public SecurityLogger(AppDbContext db, ILogger<SecurityLogger> fallback)
    {
        _db = db;
        _fallback = fallback;
    }

    public async Task LogAsync(SecurityEvent evt)
    {
        try
        {
            _db.SecurityEvents.Add(new SecurityEventEntity
            {
                Id = Guid.NewGuid(),
                ClientId = evt.ClientId,
                OccurredAt = DateTime.UtcNow,
                ActorUserId = evt.ActorUserId,
                ActorEmail = Trim(evt.ActorEmail, 200),
                IpHash = evt.IpHash,
                IpAddress = evt.IpAddress is null ? null : System.Net.IPAddress.Parse(evt.IpAddress),
                UserAgent = Trim(evt.UserAgent, 500),
                EventType = Trim(evt.EventType, 100)!,
                Severity = evt.Severity,
                Resource = Trim(evt.Resource, 200),
                Message = Trim(evt.Message, 500)!,
                Metadata = evt.Metadata is null ? null : JsonSerializer.Serialize(evt.Metadata),
                Success = evt.Success,
                ErrorCode = Trim(evt.ErrorCode, 50),
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // NEVER let logging break the request
            _fallback.LogError(ex,
                "Failed to write security event {EventType}: {Message}",
                evt.EventType, evt.Message);
        }
    }

    public string? HashIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(ip);
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    public string? HashEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant());
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static string? Trim(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length > max ? s.Substring(0, max) : s;
    }
}