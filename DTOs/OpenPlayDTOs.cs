// DTOs/OpenPlayDTOs.cs
namespace PickleballBookingSystem.DTOs;

public record CreateOpenPlaySessionRequest(
    string CourtId,
    string Date,
    string StartTime,
    string EndTime,
    int MaxPlayers,
    decimal PricePerPlayer,
    string SkillLevel,
    string? HostName,
    string? Description
);

public record UpdateOpenPlaySessionRequest(
    string CourtId,
    string Date,
    string StartTime,
    string EndTime,
    int MaxPlayers,
    decimal PricePerPlayer,
    string SkillLevel,
    string? HostName,
    string? Description,
    bool IsActive
);

public record JoinOpenPlayRequest(
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    string? Notes
);

public record OpenPlaySessionDto(
    string Id,
    string CourtId,
    string CourtName,
    string Date,
    string StartTime,
    string EndTime,
    int MaxPlayers,
    int CurrentPlayers,
    int SpotsLeft,
    decimal PricePerPlayer,
    string SkillLevel,
    string? HostName,
    string? Description,
    string Status,
    bool IsActive,
    string CreatedAt
);

public record OpenPlaySessionStatsDto(
    string Id,
    int TotalPlayers,
    int MaxPlayers,
    decimal TotalRevenue,
    decimal PendingRevenue,
    int ConfirmedCount,
    int PendingCount
);

public record OpenPlayPlayerDto(
    string BookingId,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    string ReferenceCode,
    string Status,
    string PaymentMethod,
    decimal AmountPaid,
    string JoinedAt
);