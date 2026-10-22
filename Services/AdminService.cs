using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Entities;
using PickleballBookingSystem.Interfaces;
using BCrypt.Net;

namespace PickleballBookingSystem.Services;

public class AdminService : IAdminService
{
    private readonly AppDbContext _db;

    public AdminService(AppDbContext db) => _db = db;

    // ═══════════════════════════════════════════════════════
    // ANALYTICS (single-query aggregation)
    // ═══════════════════════════════════════════════════════
    public async Task<AnalyticsDto> GetAnalyticsAsync(Guid clientId)
    {
        var stats = await _db.Bookings
            .Where(b => b.ClientId == clientId)
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Revenue = g.Sum(b => b.TotalAmount) })
            .ToListAsync();

        var map = stats.ToDictionary(s => s.Status, s => s);
        int CountOf(params string[] s) => s.Sum(x => map.TryGetValue(x, out var v) ? v.Count : 0);
        decimal RevenueOf(params string[] s) => s.Sum(x => map.TryGetValue(x, out var v) ? v.Revenue : 0);

        var totalRevenue = RevenueOf("confirmed", "completed");
        var totalBookings = stats.Sum(s => s.Count);
        var confirmedBookings = CountOf("confirmed");
        var pendingPayments = CountOf("payment_submitted");
        var completedBookings = CountOf("completed");
        var cancelledBookings = CountOf("cancelled", "rejected");

        var distinctUsers = await _db.Bookings
            .Where(b => b.ClientId == clientId)
            .Select(b => b.CustomerEmail.ToLower())
            .Distinct()
            .CountAsync();

        var revenueByDay = await _db.Bookings
            .Where(b => b.ClientId == clientId
                     && (b.Status == "confirmed" || b.Status == "completed"))
            .GroupBy(b => b.Date.Date)
            .Select(g => new RevenueByDayDto(g.Key.ToString("yyyy-MM-dd"), g.Sum(b => b.TotalAmount)))
            .OrderBy(r => r.Date).TakeLast(30).ToListAsync();

        var bookingsByDay = await _db.Bookings
            .Where(b => b.ClientId == clientId)
            .GroupBy(b => b.Date.Date)
            .Select(g => new BookingsByDayDto(g.Key.ToString("yyyy-MM-dd"), g.Count()))
            .OrderBy(b => b.Date).TakeLast(30).ToListAsync();

        return new AnalyticsDto(
            totalRevenue, totalBookings, distinctUsers,
            confirmedBookings, pendingPayments, completedBookings, cancelledBookings,
            map.ToDictionary(k => k.Key, v => v.Value.Count),
            revenueByDay, bookingsByDay,
            0, 0, 0); // growth: not computed (return 0, not a lie)
    }

    public async Task<List<CourtAnalyticsDto>> GetCourtAnalyticsAsync(Guid clientId)
    {
        var courtNames = await _db.Courts
            .Where(c => c.ClientId == clientId)
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var perCourt = await _db.Bookings
            .Where(b => b.ClientId == clientId)
            .GroupBy(b => new { b.CourtId, b.Status })
            .Select(g => new { g.Key.CourtId, g.Key.Status, Count = g.Count(), Revenue = g.Sum(b => b.TotalAmount) })
            .ToListAsync();

        var slotCounts = await _db.TimeSlots
            .Where(s => s.Booking.ClientId == clientId)
            .GroupBy(s => s.CourtId)
            .Select(g => new { CourtId = g.Key, Count = g.Count() })
            .ToListAsync();

        var slotMap = slotCounts.ToDictionary(s => s.CourtId ?? Guid.Empty, s => s.Count);

        return courtNames.Select(kv =>
        {
            var cId = kv.Key;
            var rows = perCourt.Where(p => p.CourtId == cId).ToList();
            int cnt(string st) => rows.Where(r => r.Status == st).Sum(r => r.Count);
            decimal rev(string st) => rows.Where(r => r.Status == st).Sum(r => r.Revenue);

            var totalBookings = rows.Sum(r => r.Count);
            var totalRevenue = rev("confirmed") + rev("completed");
            var confirmed = cnt("confirmed") + cnt("completed");
            var pending = cnt("payment_submitted");
            var totalSlots = slotMap.TryGetValue(cId, out var n) ? n : 0;
            var utilization = Math.Round((double)totalSlots / (12 * 30) * 100, 1);

            return new CourtAnalyticsDto(
                cId.ToString(), kv.Value, totalBookings, totalRevenue,
                confirmed, pending, utilization);
        }).ToList();
    }

    // ═══════════════════════════════════════════════════════
    // STAFF MANAGEMENT
    // ═══════════════════════════════════════════════════════
    public async Task<List<UserDto>> GetStaffByClientAsync(Guid clientId)
    {
        var staff = await _db.Users
            .Where(u => u.ClientId == clientId && u.Role == "staff")
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();
        return staff.Select(MapToUserDto).ToList();
    }

    public async Task<UserDto> CreateStaffAsync(CreateStaffRequest request, Guid clientId)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == email))
            throw new InvalidOperationException("This email is already registered.");

        if (request.Password.Length < 10)
            throw new InvalidOperationException("Password must be at least 10 characters.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Email = email,
            Phone = request.Phone?.Trim() ?? string.Empty,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = "staff",
            ClientId = clientId,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return MapToUserDto(user);
    }

    public async Task UpdateStaffStatusAsync(Guid userId, string status, Guid clientId, Guid actingUserId)
    {
        if (userId == actingUserId)
            throw new InvalidOperationException("You cannot change your own status.");

        if (status is not ("active" or "suspended"))
            throw new InvalidOperationException("Invalid status.");

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.ClientId == clientId && u.Role == "staff")
            ?? throw new KeyNotFoundException("Staff member not found");

        user.Status = status;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteStaffAsync(Guid userId, Guid clientId, Guid actingUserId)
    {
        if (userId == actingUserId)
            throw new InvalidOperationException("You cannot delete your own account.");

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.ClientId == clientId && u.Role == "staff")
            ?? throw new KeyNotFoundException("Staff member not found");

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
    }

    private static UserDto MapToUserDto(User u) => new(
        u.Id.ToString(), u.Name, u.Email, u.Phone, u.Role, u.Avatar,
        u.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"), u.BookingsCount, u.Status);
}