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

    public BookingService(AppDbContext db, EmailService email)
    {
        _db = db;
        _email = email;
    }

    public async Task<BookingDto> CreateBookingAsync(CreateBookingRequest request, Guid clientId)
    {
        // Parse court ID from string to Guid
        if (!Guid.TryParse(request.CourtId, out var courtGuid))
            throw new InvalidOperationException("Invalid court ID format");

        // Validate court exists
        var court = await _db.Courts
            .FirstOrDefaultAsync(c => c.Id == courtGuid && c.ClientId == clientId)
            ?? throw new KeyNotFoundException("Court not found");

        // Parse date - ✅ FIX: Convert to UTC
        if (!DateTime.TryParse(request.Date, out var bookingDate))
            throw new InvalidOperationException("Invalid date format");

        // ✅ Ensure date is UTC to avoid PostgreSQL timestamp issue
        bookingDate = DateTime.SpecifyKind(bookingDate.Date, DateTimeKind.Utc);

        // Validate time slots
        if (request.Slots == null || !request.Slots.Any())
            throw new InvalidOperationException("At least one time slot is required");

        // Check for conflicts
        foreach (var slot in request.Slots)
        {
            if (!TimeOnly.TryParse(slot.StartTime, out var startTime))
                throw new InvalidOperationException($"Invalid start time: {slot.StartTime}");
            if (!TimeOnly.TryParse(slot.EndTime, out var endTime))
                throw new InvalidOperationException($"Invalid end time: {slot.EndTime}");

            var conflicting = await _db.Bookings
                .Where(b => b.CourtId == courtGuid
                    && b.Date == bookingDate
                    && b.Status != "cancelled"
                    && b.Status != "expired")
                .SelectMany(b => b.Slots)
                .Where(s => s.Date == bookingDate
                    && s.StartTime < endTime
                    && s.EndTime > startTime)
                .AnyAsync();

            if (conflicting)
                throw new InvalidOperationException($"Time slot {slot.StartTime}-{slot.EndTime} is already booked");
        }

        // Check blocked dates
        var firstSlot = request.Slots.First();
        if (!TimeOnly.TryParse(firstSlot.StartTime, out var firstStartTime))
            throw new InvalidOperationException($"Invalid start time: {firstSlot.StartTime}");
        if (!TimeOnly.TryParse(firstSlot.EndTime, out var firstEndTime))
            throw new InvalidOperationException($"Invalid end time: {firstSlot.EndTime}");

        var isBlocked = await _db.BlockedDates
            .AnyAsync(bd => bd.CourtId == courtGuid
                && bd.Date == bookingDate
                && (bd.StartTime == null || bd.StartTime <= firstStartTime)
                && (bd.EndTime == null || bd.EndTime >= firstEndTime));

        if (isBlocked)
            throw new InvalidOperationException("This time slot is blocked");

        // Generate reference code
        var referenceCode = $"BK-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        var booking = new Booking
        {
            ClientId = clientId,
            CourtId = courtGuid,
            CustomerName = request.CustomerName.Trim(),
            CustomerEmail = request.CustomerEmail.Trim().ToLower(),
            CustomerPhone = request.CustomerPhone?.Trim(),
            ReferenceCode = referenceCode,
            Date = bookingDate, // ✅ Already UTC
            TotalAmount = request.TotalAmount,
            Status = "pending_payment",
            PaymentMethod = "gcash",
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow,
            PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15),
            Slots = request.Slots.Select(s => new TimeSlot
            {
                CourtId = courtGuid,
                Date = bookingDate, // ✅ UTC
                StartTime = TimeOnly.Parse(s.StartTime),
                EndTime = TimeOnly.Parse(s.EndTime),
                Price = request.TotalAmount / request.Slots.Count // Distribute total evenly
            }).ToList()
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        try
        {
            await _email.NotifyAdminNewBookingAsync(
                booking.CustomerName,
                booking.ReferenceCode,
                booking.Date.ToString("yyyy-MM-dd"),
                $"{string.Join(", ", booking.Slots.Select(s => $"{s.StartTime:HH:mm}-{s.EndTime:HH:mm}"))}",
                $"₱{booking.TotalAmount}"
            );
        }
        catch { }

        return MapToDto(booking, court.Name);
    }

    public async Task<BookingDto> GetBookingAsync(Guid id, Guid clientId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        return MapToDto(booking, booking.Court?.Name ?? "");
    }

    public async Task<BookingDto> GetBookingByReferenceAsync(string referenceCode, Guid clientId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.ReferenceCode == referenceCode && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        return MapToDto(booking, booking.Court?.Name ?? "");
    }

    public async Task<BookingDto> TrackBookingAsync(string referenceCode, string? email, Guid clientId)
    {
        var query = _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .Where(b => b.ReferenceCode == referenceCode && b.ClientId == clientId);

        if (!string.IsNullOrEmpty(email))
        {
            query = query.Where(b => b.CustomerEmail == email);
        }

        var booking = await query.FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("Booking not found");

        return MapToDto(booking, booking.Court?.Name ?? "");
    }

    public async Task<List<BookingDto>> GetBookingsAsync(Guid clientId, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .Where(b => b.ClientId == clientId);

        if (fromDate.HasValue)
            query = query.Where(b => b.Date >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(b => b.Date <= toDate.Value);

        var bookings = await query
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        return bookings.Select(b => MapToDto(b, b.Court?.Name ?? "")).ToList();
    }

    public async Task<List<BookingDto>> GetAllBookingsAsync(Guid clientId)
    {
        var bookings = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .Where(b => b.ClientId == clientId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        return bookings.Select(b => MapToDto(b, b.Court?.Name ?? "")).ToList();
    }

    public async Task<BookingDto> UpdateBookingStatusAsync(Guid id, string status, Guid clientId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        booking.Status = status;
        await _db.SaveChangesAsync();

        return MapToDto(booking, booking.Court?.Name ?? "");
    }

    public async Task<BookingDto> AdminUpdateBookingAsync(Guid id, AdminUpdateBookingRequest request, Guid clientId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        booking.Status = request.Status;
        await _db.SaveChangesAsync();

        return MapToDto(booking, booking.Court?.Name ?? "");
    }

    public async Task<BookingDto> UploadPaymentAsync(Guid id, string screenshotBase64, string? paymentReference, Guid clientId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        if (booking.Status != "pending_payment")
            throw new InvalidOperationException("Booking is not pending payment");

        booking.PaymentScreenshot = screenshotBase64;
        booking.PaymentReference = paymentReference;
        booking.Status = "payment_submitted";
        await _db.SaveChangesAsync();

        return MapToDto(booking, booking.Court?.Name ?? "");
    }

    public async Task<BookingDto> UploadPaymentScreenshotAsync(Guid id, string screenshotBase64, string? paymentReference, Guid clientId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        if (booking.Status != "pending_payment")
            throw new InvalidOperationException("Booking is not pending payment");

        booking.PaymentScreenshot = screenshotBase64;
        booking.PaymentReference = paymentReference;
        booking.Status = "payment_submitted";
        await _db.SaveChangesAsync();

        return MapToDto(booking, booking.Court?.Name ?? "");
    }

    public async Task ConfirmPaymentAsync(Guid id, Guid clientId)
    {
        var booking = await _db.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        booking.Status = "confirmed";
        await _db.SaveChangesAsync();
    }

    public async Task CancelBookingAsync(Guid id, Guid clientId)
    {
        var booking = await _db.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        booking.Status = "cancelled";
        await _db.SaveChangesAsync();
    }

    public async Task AutoCompletePastBookingsAsync(Guid clientId)
    {
        var pastBookings = await _db.Bookings
            .Where(b => b.ClientId == clientId
                && b.Date < DateTime.UtcNow.Date
                && b.Status != "completed"
                && b.Status != "cancelled")
            .ToListAsync();

        foreach (var booking in pastBookings)
        {
            booking.Status = "completed";
        }

        await _db.SaveChangesAsync();
    }

    public async Task CancelExpiredPaymentsAsync(Guid clientId)
    {
        var expiredBookings = await _db.Bookings
            .Where(b => b.ClientId == clientId
                && b.Status == "pending_payment"
                && b.PaymentExpiresAt != null
                && b.PaymentExpiresAt < DateTime.UtcNow)
            .ToListAsync();

        foreach (var booking in expiredBookings)
        {
            booking.Status = "expired";
        }

        await _db.SaveChangesAsync();
    }

    private static BookingDto MapToDto(Booking b, string courtName)
    {
        return new BookingDto(
            b.Id.ToString(),
            b.CourtId.ToString(),
            courtName,
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
                false, // IsAvailable - always false for booked slots
                s.Price
            )).ToList(),
            b.TotalAmount,
            b.Status,
            b.PaymentMethod,
            b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            b.Notes,
            b.PaymentScreenshot,
            b.PaymentExpiresAt,
            b.PaymentReference
        );
    }
}