using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Entities;
using PickleballBookingSystem.Interfaces;

namespace PickleballBookingSystem.Services;

public class BookingService : IBookingService
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly IConfiguration _config;

    public BookingService(AppDbContext db, EmailService email, IConfiguration config)
    {
        _db = db;
        _email = email;
        _config = config;
    }

    // ✅ Helper method to calculate slot price
    private static decimal CalculateSlotPrice(Court court, TimeOnly startTime, TimeOnly endTime, DateTime date)
    {
        // Check if it's peak hours (5 PM - 9 PM)
        var isPeak = startTime >= new TimeOnly(17, 0) && endTime <= new TimeOnly(21, 0);

        // Check if it's weekend (Saturday or Sunday)
        var isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;

        var basePrice = isPeak ? court.PeakPricePerHour : court.PricePerHour;
        if (isWeekend) basePrice *= 1.2m; // 20% weekend surcharge

        var hours = (decimal)(endTime - startTime).TotalHours;
        return Math.Round(basePrice * hours, 2);
    }

    public async Task<BookingDto> CreateBookingAsync(CreateBookingRequest request, Guid clientId)
    {
        var bookingDate = DateTime.SpecifyKind(DateTime.Parse(request.Date).Date, DateTimeKind.Utc);

        // ✅ Validate court exists and belongs to client
        var court = await _db.Courts
            .FirstOrDefaultAsync(c => c.Id == Guid.Parse(request.CourtId) && c.ClientId == clientId)
            ?? throw new KeyNotFoundException("Court not found");

        // ✅ Check availability for this specific court
        if (!request.AdminOverride)
        {
            var requestedStartTimes = request.Slots.Select(s => TimeOnly.Parse(s.StartTime)).ToHashSet();
            var conflictingBookings = await _db.Bookings
                .Where(b => b.Date.Date == bookingDate.Date
                    && b.CourtId == court.Id
                    && b.ClientId == clientId
                    && b.Status != "cancelled"
                    && b.Status != "expired")
                .Include(b => b.Slots)
                .ToListAsync();

            var bookedTimes = conflictingBookings
                .SelectMany(b => b.Slots)
                .Select(s => s.StartTime)
                .ToHashSet();

            if (requestedStartTimes.Any(t => bookedTimes.Contains(t)))
                throw new InvalidOperationException("One or more selected time slots are no longer available.");
        }

        var referenceCode = $"PJ-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        var bookingStatus = request.Status ?? "pending_payment";

        var booking = new Booking
        {
            CourtId = court.Id,
            ClientId = clientId,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            CustomerPhone = request.CustomerPhone,
            ReferenceCode = referenceCode,
            Date = bookingDate,
            TotalAmount = request.TotalAmount,
            Status = bookingStatus,
            PaymentMethod = bookingStatus == "confirmed" ? "cash" : "gcash",
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            // ✅ Store price per slot
            Slots = request.Slots.Select(s => new TimeSlot
            {
                CourtId = court.Id,
                Date = bookingDate,
                StartTime = TimeOnly.Parse(s.StartTime),
                EndTime = TimeOnly.Parse(s.EndTime),
                Price = CalculateSlotPrice(court, TimeOnly.Parse(s.StartTime), TimeOnly.Parse(s.EndTime), bookingDate)
            }).ToList()
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        return MapToDto(booking);
    }

    public async Task<BookingDto?> TrackBookingAsync(string referenceCode, string email, Guid clientId)
    {
        var query = _db.Bookings.Where(b => b.ClientId == clientId);

        if (!string.IsNullOrEmpty(referenceCode) && referenceCode != "ANY")
            query = query.Where(b => b.ReferenceCode == referenceCode);

        if (!string.IsNullOrEmpty(email) && email != "ANY")
            query = query.Where(b => b.CustomerEmail == email);

        return await query
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .Select(b => MapToDto(b))
            .FirstOrDefaultAsync();
    }

    public async Task<List<BookingDto>> GetAllBookingsAsync(Guid clientId) =>
        await _db.Bookings
            .Where(b => b.ClientId == clientId)
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .OrderByDescending(b => b.Date)
            .Select(b => MapToDto(b))
            .ToListAsync();

    public async Task<BookingDto> AdminUpdateBookingAsync(Guid id, AdminUpdateBookingRequest request, Guid clientId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        booking.Status = request.Status;
        await _db.SaveChangesAsync();

        // Send email to customer when confirmed
        if (request.Status == "confirmed" && !string.IsNullOrEmpty(booking.CustomerEmail))
        {
            var timeDisplay = booking.Slots.Any()
                ? $"{booking.Slots.OrderBy(s => s.StartTime).First().StartTime}–{booking.Slots.OrderBy(s => s.StartTime).Last().EndTime}"
                : "";
            _ = Task.Run(async () =>
            {
                try
                {
                    await _email.NotifyCustomerBookingConfirmedAsync(
                        booking.CustomerEmail,
                        booking.CustomerName,
                        booking.ReferenceCode,
                        booking.Date.ToString("yyyy-MM-dd"),
                        timeDisplay
                    );
                }
                catch { }
            });
        }

        return MapToDto(booking);
    }

    public async Task<BookingDto> UploadPaymentScreenshotAsync(Guid id, string screenshotUrl, Guid clientId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        booking.PaymentScreenshot = screenshotUrl;
        booking.Status = "payment_submitted";
        await _db.SaveChangesAsync();

        // Notify admin
        try
        {
            await _email.NotifyAdminNewBookingAsync(
                booking.CustomerName,
                booking.ReferenceCode + " [PAYMENT]",
                booking.Date.ToString("yyyy-MM-dd"),
                "Screenshot uploaded",
                $"₱{booking.TotalAmount}"
            );
        }
        catch { }

        return MapToDto(booking);
    }

    public async Task AutoCompletePastBookingsAsync(Guid clientId)
    {
        var now = DateTime.UtcNow;
        var pastConfirmed = await _db.Bookings
            .Where(b => b.Status == "confirmed" && b.ClientId == clientId)
            .Include(b => b.Slots)
            .ToListAsync();

        foreach (var booking in pastConfirmed)
        {
            var lastSlot = booking.Slots.OrderByDescending(s => s.EndTime).FirstOrDefault();
            if (lastSlot == null) continue;
            var bookingEnd = booking.Date.Date.Add(lastSlot.EndTime.ToTimeSpan());
            if (bookingEnd < now) booking.Status = "completed";
        }
        await _db.SaveChangesAsync();
    }

    public async Task CancelExpiredPaymentsAsync(Guid clientId)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.Bookings
            .Where(b => b.Status == "pending_payment" && b.PaymentExpiresAt < now && b.ClientId == clientId)
            .ToListAsync();

        foreach (var booking in expired)
        {
            booking.Status = "expired";
        }
        await _db.SaveChangesAsync();
    }

    // ✅ Updated MapToDto with Price
    private static BookingDto MapToDto(Booking b) => new(
        b.Id.ToString(),
        b.CourtId.ToString(),
        b.Court?.Name ?? "",
        b.CustomerName,
        b.CustomerEmail,
        b.CustomerPhone,
        b.ReferenceCode,
        b.Date.ToString("yyyy-MM-dd"),
        b.Slots.Select(s => new TimeSlotDto(
            s.Id.ToString(),
            s.Date.ToString("yyyy-MM-dd"),
            s.StartTime.ToString("HH:mm"),
            s.EndTime.ToString("HH:mm"),
            false,
            s.Price // ✅ Include the stored price
        )).ToList(),
        b.TotalAmount,
        b.Status,
        b.PaymentMethod,
        b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        b.Notes,
        b.PaymentScreenshot,
        b.PaymentExpiresAt
    );
}