public async Task<AnalyticsDto> GetAnalyticsAsync(Guid clientId)
{
    var bookings = await _db.Bookings
        .Where(b => b.ClientId == clientId)
        .ToListAsync();

    var confirmedOrCompleted = bookings
        .Where(b => b.Status == "confirmed" || b.Status == "completed")
        .ToList();

    var totalRevenue = confirmedOrCompleted.Sum(b => b.TotalAmount);
    var totalBookings = bookings.Count;
    var activeUsers = bookings.Select(b => b.CustomerEmail).Distinct().Count();

    // ✅ NEW - status counts
    var confirmedBookings = bookings.Count(b => b.Status == "confirmed");
    var pendingPayments = bookings.Count(b => b.Status == "pending_payment" || b.Status == "payment_submitted");
    var completedBookings = bookings.Count(b => b.Status == "completed");
    var cancelledBookings = bookings.Count(b => b.Status == "cancelled" || b.Status == "rejected");

    // ✅ NEW - full breakdown by exact status string
    var statusBreakdown = bookings
        .GroupBy(b => b.Status)
        .ToDictionary(g => g.Key, g => g.Count());

    var revenueByDay = confirmedOrCompleted
        .GroupBy(b => b.Date.Date)
        .Select(g => new RevenueByDayDto(g.Key.ToString("yyyy-MM-dd"), g.Sum(b => b.TotalAmount)))
        .OrderBy(r => r.Date).TakeLast(30).ToList();

    var bookingsByDay = bookings
        .GroupBy(b => b.Date.Date)
        .Select(g => new BookingsByDayDto(g.Key.ToString("yyyy-MM-dd"), g.Count()))
        .OrderBy(b => b.Date).TakeLast(30).ToList();

    return new AnalyticsDto(
        totalRevenue,
        totalBookings,
        activeUsers,
        confirmedBookings,
        pendingPayments,
        completedBookings,
        cancelledBookings,
        statusBreakdown,
        revenueByDay,
        bookingsByDay,
        12.5,
        8.3,
        5.1
    );
}