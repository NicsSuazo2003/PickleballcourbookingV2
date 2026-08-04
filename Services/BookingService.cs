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

    public async Task<BookingDto> CreateBookingAsync(CreateBookingRequest request)
    {
        var bookingDate = DateTime.SpecifyKind(DateTime.Parse(request.Date).Date, DateTimeKind.Utc);

        // ✅ Validate court exists
        var court = await _db.Courts.FindAsync(Guid.Parse(request.CourtId))
            ?? throw new KeyNotFoundException("Court not found");

        // ✅ Check availability for this specific court
        if (!request.AdminOverride)
        {
            var requestedStartTimes = request.Slots.Select(s => TimeOnly.Parse(s.StartTime)).ToHashSet();
            var conflictingBookings = await _db.Bookings
                .Where(b => b.Date.Date == bookingDate.Date
                    && b.CourtId == court.Id
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

        var referenceCode = $"SOP-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        var bookingStatus = request.Status ?? "pending_payment";

        var booking = new Booking
        {
            CourtId = court.Id,  // ✅ NEW
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
            Slots = request.Slots.Select(s => new TimeSlot
            {
                CourtId = court.Id,  // ✅ NEW
                Date = bookingDate,
                StartTime = TimeOnly.Parse(s.StartTime),
                EndTime = TimeOnly.Parse(s.EndTime)
            }).ToList()
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        return MapToDto(booking);
    }

    public async Task<BookingDto?> TrackBookingAsync(string referenceCode, string email)
    {
        var query = _db.Bookings.AsQueryable();

        if (!string.IsNullOrEmpty(referenceCode) && referenceCode != "ANY")
            query = query.Where(b => b.ReferenceCode == referenceCode);

        if (!string.IsNullOrEmpty(email) && email != "ANY")
            query = query.Where(b => b.CustomerEmail == email);

        return await query
            .Include(b => b.Slots)
            .Include(b => b.Court)  // ✅ Include court info
            .Select(b => MapToDto(b))
            .FirstOrDefaultAsync();
    }

    public async Task<List<BookingDto>> GetAllBookingsAsync() =>
        await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)  // ✅ Include court info
            .OrderByDescending(b => b.Date)
            .Select(b => MapToDto(b))
            .ToListAsync();

    public async Task<BookingDto> AdminUpdateBookingAsync(Guid id, AdminUpdateBookingRequest request)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)  // ✅ Include court info
            .FirstOrDefaultAsync(b => b.Id == id)
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

    public async Task<BookingDto> UploadPaymentScreenshotAsync(Guid id, string screenshotUrl)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)  // ✅ Include court info
            .FirstOrDefaultAsync(b => b.Id == id)
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

    public async Task AutoCompletePastBookingsAsync()
    {
        var now = DateTime.UtcNow;
        var pastConfirmed = await _db.Bookings
            .Where(b => b.Status == "confirmed")
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

    public async Task CancelExpiredPaymentsAsync()
    {
        var now = DateTime.UtcNow;
        var expired = await _db.Bookings
            .Where(b => b.Status == "pending_payment" && b.PaymentExpiresAt < now)
            .ToListAsync();

        foreach (var booking in expired)
        {
            booking.Status = "expired";
        }
        await _db.SaveChangesAsync();
    }

    // ========================================
    // ✅ UPDATED MapToDto with Court info
    // ========================================

    private static BookingDto MapToDto(Booking b) => new(
        b.Id.ToString(),
        b.CourtId.ToString(),        // ✅ NEW
        b.Court?.Name ?? "",         // ✅ NEW
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
            false
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