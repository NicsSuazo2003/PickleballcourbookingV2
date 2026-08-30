namespace PickleballBookingSystem.DTOs;

public record AnalyticsDto(
    decimal TotalRevenue,
    int TotalBookings,
    int ActiveUsers,
    int ConfirmedBookings,
    int PendingPayments,
    int CompletedBookings,
    int CancelledBookings,
    Dictionary<string, int> StatusBreakdown,
    List<RevenueByDayDto> RevenueByDay,
    List<BookingsByDayDto> BookingsByDay,
    double RevenueGrowth,
    double BookingsGrowth,
    double UsersGrowth
);

// ✅ NEW - Per-court analytics
public record CourtAnalyticsDto(
    string CourtId,
    string CourtName,
    int TotalBookings,
    decimal TotalRevenue,
    int ConfirmedBookings,
    int PendingBookings,
    double UtilizationRate
);

public record RevenueByDayDto(string Date, decimal Revenue);
public record BookingsByDayDto(string Date, int Bookings);
public record AdminUpdateUserRequest(string? Name, string? Email, string? Role, string? Status);