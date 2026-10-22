using System.ComponentModel.DataAnnotations;

namespace PickleballBookingSystem.DTOs;

public record PaymentMethodConfigDto(
    string AccountNumber,
    string AccountName,
    string? QrImageUrl,
    string? Instructions
);

public record PaymentMethodDto(
    string Id,
    string Name,
    string Icon,
    bool Enabled,
    PaymentMethodConfigDto Config
);

public record AmenityItemDto(
    [Required, MaxLength(60)] string Name,
    [MaxLength(40)] string? Icon,
    [MaxLength(300)] string? Description
);

public record UpdateClientSettingsRequest(
    [MaxLength(120)] string? Name,
    [MaxLength(20)] string? GcashNumber,
    [MaxLength(120)] string? GcashAccountName,
    List<PaymentMethodDto>? PaymentMethods,
    List<AmenityItemDto>? AvailableAmenities,
    [Range(0, 365)] int? MaxAdvanceBookingDays,
    bool? BookingsEnabled
);

public record CreateStaffRequest(
    [Required, MaxLength(80)] string Name,
    [Required, EmailAddress, MaxLength(120)] string Email,
    [Phone, MaxLength(20)] string Phone,
    [Required, MinLength(10), MaxLength(128)] string Password
);

public record UpdateStaffStatusRequest(
    [Required, RegularExpression("^(active|suspended)$")] string Status
);

public record UserDto(
    string Id, string Name, string Email, string Phone, string Role,
    string? Avatar, string CreatedAt, int BookingsCount, string Status
);

public record AnalyticsDto(
    decimal TotalRevenue, int TotalBookings, int ActiveUsers,
    int ConfirmedBookings, int PendingPayments, int CompletedBookings, int CancelledBookings,
    Dictionary<string, int> StatusBreakdown,
    List<RevenueByDayDto> RevenueByDay, List<BookingsByDayDto> BookingsByDay,
    double RevenueGrowth, double BookingsGrowth, double UsersGrowth
);

public record CourtAnalyticsDto(
    string CourtId, string CourtName, int TotalBookings, decimal TotalRevenue,
    int ConfirmedBookings, int PendingBookings, double UtilizationRate
);

public record RevenueByDayDto(string Date, decimal Revenue);
public record BookingsByDayDto(string Date, int Bookings);
public record AdminUpdateUserRequest(string? Name, string? Email, string? Role, string? Status);