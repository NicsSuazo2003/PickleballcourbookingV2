namespace PickleballBookingSystem.DTOs;

public record CourtDto(
    string Id,
    string Name,
    string Type,
    bool Indoor,
    decimal PricePerHour,
    decimal PeakPricePerHour,
    string? Description,
    List<string> Amenities,
    double Rating,
    string ImageUrl,
    List<string> Images,
    string Status,
    string OpenTime,
    string CloseTime,
    string Dimensions,
    string Surface
);

public record CreateCourtRequest(
    string Name,
    string Type,
    bool Indoor,
    decimal PricePerHour,
    decimal PeakPricePerHour,
    string? Description,
    List<string>? Amenities,
    string OpenTime,
    string CloseTime,
    string? Dimensions,
    string? Surface
);

public record UpdateCourtRequest(
    string? Name,
    string? Type,
    bool? Indoor,
    decimal? PricePerHour,
    decimal? PeakPricePerHour,
    string? Description,
    List<string>? Amenities,
    string? ImageUrl,
    List<string>? Images,
    string? Status,
    string? OpenTime,
    string? CloseTime,
    string? Dimensions,
    string? Surface
);

public record TimeSlotAvailabilityDto(
    string Id,
    string Date,
    string StartTime,
    string EndTime,
    bool IsAvailable,
    decimal Price,
    bool IsPeak,
    string Status,              // 👈 NEW: "available" | "booked" | "blocked" | "pending" | "open_play" | "past"
    DateTime? PendingExpiresAt  // 👈 NEW: only set when Status == "pending"
);

public record BlockedDateDto(
    string Id,
    string Date,
    string? StartTime,
    string? EndTime,
    string? Reason
);

// Ensure PascalCase properties so ASP.NET binder works seamlessly
public record CreateBlockedDateRequest(
    string Date,
    string? StartTime,
    string? EndTime,
    string? Reason
);