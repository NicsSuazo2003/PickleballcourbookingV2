namespace PickleballBookingSystem.DTOs;

public record SecurityEventDto(
    string Id,
    DateTime OccurredAt,
    string EventType,
    string Severity,
    string Message,
    string? ActorEmail,
    string? IpHash,
    string? Resource,
    bool Success,
    string? ErrorCode
);