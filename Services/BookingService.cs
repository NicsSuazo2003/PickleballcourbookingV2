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
    private readonly IPricingRuleService _pricingRuleService;

    public BookingService(AppDbContext db, EmailService email, IPricingRuleService pricingRuleService)
    {
        _db = db;
        _email = email;
        _pricingRuleService = pricingRuleService;
    }

    // ⭐ NEW — validates the requested booking date against the client's
    // max advance booking window. 0 = no limit.
    private async Task EnsureWithinAdvanceWindowAsync(Guid clientId, DateTime bookingDate)
    {
        var client = await _db.Clients.FindAsync(clientId);
        if (client is null) return;

        if (client.MaxAdvanceBookingDays > 0)
        {
            var maxDate = DateTime.UtcNow.Date.AddDays(client.MaxAdvanceBookingDays);
            if (bookingDate.Date > maxDate)
            {
                throw new InvalidOperationException(
                    $"Bookings can only be made up to {client.MaxAdvanceBookingDays} days in advance.");
            }
        }
    }

    public async Task<BookingDto> CreateBookingAsync(CreateBookingRequest request, Guid clientId)
    {
        if (!Guid.TryParse(request.CourtId, out var courtGuid))
            throw new InvalidOperationException("Invalid court ID format");

        var court = await _db.Courts
            .FirstOrDefaultAsync(c => c.Id == courtGuid && c.ClientId == clientId)
            ?? throw new KeyNotFoundException("Court not found");

        if (!DateTime.TryParse(request.Date, out var bookingDate))
            throw new InvalidOperationException("Invalid date format");

        bookingDate = DateTime.SpecifyKind(bookingDate.Date, DateTimeKind.Utc);

        await EnsureWithinAdvanceWindowAsync(clientId, bookingDate);

        if (request.Slots == null || !request.Slots.Any())
            throw new InvalidOperationException("At least one time slot is required");

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
                    && b.Status != "expired"
                    && b.Status != "rejected"
                    && b.Status != "refunded")
                .SelectMany(b => b.Slots)
                .Where(s => s.Date == bookingDate
                    && s.StartTime < endTime
                    && s.EndTime > startTime)
                .AnyAsync();

            if (conflicting)
                throw new InvalidOperationException($"Time slot {slot.StartTime}-{slot.EndTime} is already booked");
        }

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

        var referenceCode = $"BK-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        var courtWithRules = await _db.Courts
            .Include(c => c.PricingRules)
            .FirstAsync(c => c.Id == courtGuid);

        var dateOnly = DateOnly.FromDateTime(bookingDate);

        decimal computedTotal = 0m;
        var slotPrices = new List<decimal>();

        foreach (var slot in request.Slots)
        {
            if (!TimeOnly.TryParse(slot.StartTime, out var slotStart))
                throw new InvalidOperationException($"Invalid start time: {slot.StartTime}");
            if (!TimeOnly.TryParse(slot.EndTime, out var slotEnd))
                throw new InvalidOperationException($"Invalid end time: {slot.EndTime}");

            var hours = (decimal)(slotEnd - slotStart).TotalHours;
            var ratePerHour = _pricingRuleService.ResolvePriceFromCourt(
                courtWithRules, dateOnly, slotStart);

            var slotPrice = Math.Round(ratePerHour * hours, 2);
            slotPrices.Add(slotPrice);
            computedTotal += slotPrice;
        }

        var booking = new Booking
        {
            ClientId = clientId,
            CourtId = courtGuid,
            CustomerName = request.CustomerName.Trim(),
            CustomerEmail = request.CustomerEmail.Trim().ToLower(),
            CustomerPhone = request.CustomerPhone?.Trim(),
            ReferenceCode = referenceCode,
            Date = bookingDate,
            TotalAmount = computedTotal,
            Status = "pending_payment",
            PaymentMethod = "gcash",
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow,
            PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15),
            Slots = request.Slots
                .Select((s, index) => new TimeSlot
                {
                    CourtId = courtGuid,
                    Date = bookingDate,
                    StartTime = TimeOnly.Parse(s.StartTime),
                    EndTime = TimeOnly.Parse(s.EndTime),
                    Price = slotPrices[index]
                })
                .ToList()
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
                $"₱{booking.TotalAmount:N2}"
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

        var previousStatus = booking.Status;
        booking.Status = status;
        await _db.SaveChangesAsync();

        await SendStatusChangeEmailAsync(booking, previousStatus);

        return MapToDto(booking, booking.Court?.Name ?? "");
    }

    public async Task<BookingDto> AdminUpdateBookingAsync(Guid id, AdminUpdateBookingRequest request, Guid clientId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        var previousStatus = booking.Status;
        booking.Status = request.Status;
        await _db.SaveChangesAsync();

        await SendStatusChangeEmailAsync(booking, previousStatus);

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

    public async Task<BookingDto> UploadPaymentScreenshotAsync(
     Guid id,
     string? screenshotUrl,
     string? paymentReference,
     string? paymentMethod,
     Guid clientId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        if (booking.Status != "pending_payment")
            throw new InvalidOperationException("Booking is not pending payment");

        if (!string.IsNullOrEmpty(screenshotUrl))
            booking.PaymentScreenshot = screenshotUrl;

        booking.PaymentReference = paymentReference;

        if (!string.IsNullOrWhiteSpace(paymentMethod))
            booking.PaymentMethod = paymentMethod;

        booking.Status = "payment_submitted";
        await _db.SaveChangesAsync();

        return MapToDto(booking, booking.Court?.Name ?? "");
    }

    public async Task ConfirmPaymentAsync(Guid id, Guid clientId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        var previousStatus = booking.Status;
        booking.Status = "confirmed";
        await _db.SaveChangesAsync();

        await SendStatusChangeEmailAsync(booking, previousStatus);
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
                && b.Status != "cancelled"
                && b.Status != "rejected"
                && b.Status != "refunded"
                && b.Status != "expired")
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

    private async Task SendStatusChangeEmailAsync(Booking booking, string previousStatus)
    {
        if (previousStatus == booking.Status)
            return;

        var timeRange = string.Join(", ", booking.Slots
            .OrderBy(s => s.StartTime)
            .Select(s => $"{s.StartTime:HH:mm}-{s.EndTime:HH:mm}"));
        var dateStr = booking.Date.ToString("yyyy-MM-dd");
        var amountStr = $"₱{booking.TotalAmount:N2}";

        try
        {
            if (booking.Status == "confirmed")
            {
                await _email.NotifyCustomerBookingConfirmedAsync(
                    booking.CustomerEmail,
                    booking.CustomerName,
                    booking.ReferenceCode,
                    dateStr,
                    timeRange,
                    amountStr
                );
            }
            else if (booking.Status == "rejected")
            {
                await _email.NotifyCustomerBookingRejectedAsync(
                    booking.CustomerEmail,
                    booking.CustomerName,
                    booking.ReferenceCode,
                    dateStr,
                    timeRange,
                    null,
                    amountStr
                );
            }
            else if (booking.Status == "cancelled")
            {
                await _email.NotifyCustomerBookingCancelledAsync(
                    booking.CustomerEmail,
                    booking.CustomerName,
                    booking.ReferenceCode,
                    dateStr,
                    timeRange,
                    null,
                    amountStr
                );
            }
            else if (booking.Status == "refunded" && booking.TotalAmount > 0)
            {
                await _email.NotifyCustomerBookingRefundedAsync(
                    booking.CustomerEmail,
                    booking.CustomerName,
                    booking.ReferenceCode,
                    dateStr,
                    timeRange,
                    amountStr
                );
            }
        }
        catch
        {
            // Never let an email failure fail the underlying status update.
        }
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
                false,
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

    public async Task<BookingDto> CreateStaffBookingAsync(StaffCreateBookingRequest request, Guid clientId)
    {
        if (!Guid.TryParse(request.CourtId, out var courtGuid))
            throw new InvalidOperationException("Invalid court ID format");

        var court = await _db.Courts
            .FirstOrDefaultAsync(c => c.Id == courtGuid && c.ClientId == clientId)
            ?? throw new KeyNotFoundException("Court not found");

        if (!DateTime.TryParse(request.Date, out var bookingDate))
            throw new InvalidOperationException("Invalid date format");

        await EnsureWithinAdvanceWindowAsync(clientId, bookingDate);

        if (request.Slots == null || !request.Slots.Any())
            throw new InvalidOperationException("At least one time slot is required");

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
                    && b.Status != "expired"
                    && b.Status != "rejected"
                    && b.Status != "refunded")
                .SelectMany(b => b.Slots)
                .Where(s => s.Date == bookingDate
                    && s.StartTime < endTime
                    && s.EndTime > startTime)
                .AnyAsync();

            if (conflicting)
                throw new InvalidOperationException($"Time slot {slot.StartTime}-{slot.EndTime} is already booked");
        }

        var mode = (request.PaymentMode ?? "cash").ToLowerInvariant();
        string status;
        string paymentMethod;
        DateTime? expiresAt;

        switch (mode)
        {
            case "gcash":
                status = "pending_payment";
                paymentMethod = "gcash";
                expiresAt = DateTime.UtcNow.AddMinutes(15);
                break;

            case "pay_later":
                status = "pending_payment";
                paymentMethod = "cash";
                expiresAt = null;
                break;

            case "free":
            case "comp":
                status = "confirmed";
                paymentMethod = "comp";
                expiresAt = null;
                break;

            case "cash":
            default:
                status = "confirmed";
                paymentMethod = "cash";
                expiresAt = null;
                break;
        }

        decimal totalAmount;
        if (request.TotalAmount.HasValue && request.TotalAmount.Value >= 0)
        {
            totalAmount = request.TotalAmount.Value;
        }
        else
        {
            var courtWithRules = await _db.Courts
                .Include(c => c.PricingRules)
                .FirstAsync(c => c.Id == courtGuid);

            var dateOnly = DateOnly.FromDateTime(bookingDate);

            decimal sum = 0m;
            foreach (var slot in request.Slots)
            {
                if (!TimeOnly.TryParse(slot.StartTime, out var st)) continue;
                if (!TimeOnly.TryParse(slot.EndTime, out var en)) continue;

                var hours = (decimal)(en - st).TotalHours;
                var ratePerHour = _pricingRuleService.ResolvePriceFromCourt(courtWithRules, dateOnly, st);
                sum += ratePerHour * hours;
            }
            totalAmount = sum;
        }

        var referenceCode = $"ST-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        var booking = new Booking
        {
            ClientId = clientId,
            CourtId = courtGuid,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail ?? "",
            CustomerPhone = request.CustomerPhone,
            ReferenceCode = referenceCode,
            Date = bookingDate,
            TotalAmount = totalAmount,
            Status = status,
            PaymentMethod = paymentMethod,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            PaymentExpiresAt = expiresAt,
            Slots = request.Slots.Select(s => new TimeSlot
            {
                CourtId = courtGuid,
                Date = bookingDate,
                StartTime = TimeOnly.Parse(s.StartTime),
                EndTime = TimeOnly.Parse(s.EndTime),
                Price = totalAmount / request.Slots.Count
            }).ToList()
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        if (request.SendConfirmation && !string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            try
            {
                await _email.NotifyCustomerBookingConfirmedAsync(
                    booking.CustomerEmail,
                    booking.CustomerName,
                    booking.ReferenceCode,
                    booking.Date.ToString("yyyy-MM-dd"),
                    string.Join(", ", booking.Slots.Select(s => $"{s.StartTime:HH:mm} - {s.EndTime:HH:mm}")),
                    $"₱{booking.TotalAmount:N2}"
                );
            }
            catch { }
        }

        return MapToDto(booking, court.Name);
    }

    // ═════════════════════════════════════════════════════════════
    // ✅ RESCHEDULE — move a booking to a new court / date / time
    // ═════════════════════════════════════════════════════════════
    public async Task<RescheduleBookingResponse> RescheduleBookingAsync(
        Guid id,
        RescheduleBookingRequest request,
        Guid clientId)
    {
        if (request.Slots == null || !request.Slots.Any())
            throw new InvalidOperationException("At least one time slot is required");

        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        if (booking.Status is "cancelled" or "rejected" or "expired" or "refunded")
            throw new InvalidOperationException(
                $"Cannot reschedule a booking that is already {booking.Status}");

        // ── Target court (default: same) ─────────────────────────
        var targetCourtId = booking.CourtId;
        if (!string.IsNullOrWhiteSpace(request.CourtId))
        {
            if (!Guid.TryParse(request.CourtId, out var parsedCourt))
                throw new InvalidOperationException("Invalid court ID format");
            targetCourtId = parsedCourt;
        }

        var targetCourt = await _db.Courts
            .Include(c => c.PricingRules)
            .FirstOrDefaultAsync(c => c.Id == targetCourtId && c.ClientId == clientId)
            ?? throw new KeyNotFoundException("Target court not found");

        // ── Target date (default: same) ──────────────────────────
        var targetDate = booking.Date;
        if (!string.IsNullOrWhiteSpace(request.Date))
        {
            if (!DateTime.TryParse(request.Date, out var parsedDate))
                throw new InvalidOperationException("Invalid date format");
            targetDate = DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Utc);
        }

        await EnsureWithinAdvanceWindowAsync(clientId, targetDate);

        // ── Normalize + validate new slots ───────────────────────
        var newSlots = new List<(TimeOnly Start, TimeOnly End)>();
        foreach (var s in request.Slots)
        {
            if (!TimeOnly.TryParse(s.StartTime, out var st))
                throw new InvalidOperationException($"Invalid start time: {s.StartTime}");
            if (!TimeOnly.TryParse(s.EndTime, out var en))
                throw new InvalidOperationException($"Invalid end time: {s.EndTime}");
            if (en <= st)
                throw new InvalidOperationException(
                    $"Slot end must be after start: {s.StartTime}-{s.EndTime}");
            newSlots.Add((st, en));
        }

        var ordered = newSlots.OrderBy(s => s.Start).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start < ordered[i - 1].End)
                throw new InvalidOperationException("New slots overlap each other");
        }

        // ── Within court hours ───────────────────────────────────
        var openHour = targetCourt.OpenTime.Hour;
        var closeHour = targetCourt.CloseTime.Hour == 0 ? 24 : targetCourt.CloseTime.Hour;

        foreach (var (start, end) in ordered)
        {
            var endHour = end.Hour == 0 ? 24 : end.Hour;
            if (start.Hour < openHour || endHour > closeHour)
                throw new InvalidOperationException(
                    $"{start:HH\\:mm}-{end:HH\\:mm} is outside this court's operating hours");
        }

        // ── Conflict check (exclude this booking) ────────────────
        foreach (var (start, end) in ordered)
        {
            var conflicting = await _db.Bookings
                .Where(b => b.Id != booking.Id
                         && b.CourtId == targetCourtId
                         && b.Date == targetDate
                         && b.Status != "cancelled"
                         && b.Status != "expired"
                         && b.Status != "rejected"
                         && b.Status != "refunded")
                .SelectMany(b => b.Slots)
                .Where(s => s.Date == targetDate
                         && s.StartTime < end
                         && s.EndTime > start)
                .AnyAsync();

            if (conflicting)
                throw new InvalidOperationException(
                    $"Time slot {start:HH\\:mm}-{end:HH\\:mm} is already booked on this court");
        }

        // ── Blocked-date check ───────────────────────────────────
        var blocked = await _db.BlockedDates
            .Where(bd => bd.ClientId == clientId
                      && bd.Date == targetDate
                      && (bd.CourtId == null || bd.CourtId == targetCourtId))
            .ToListAsync();

        foreach (var bd in blocked)
        {
            if (bd.StartTime == null)
                throw new InvalidOperationException("This date is fully blocked for that court");

            var blockStart = bd.StartTime.Value;
            var blockEnd = bd.EndTime ?? new TimeOnly(23, 59);

            foreach (var (start, end) in ordered)
            {
                if (start < blockEnd && end > blockStart)
                    throw new InvalidOperationException(
                        $"Time slot {start:HH\\:mm}-{end:HH\\:mm} overlaps a blocked window");
            }
        }

        // ── Snapshot previous state ──────────────────────────────
        var previousSlots = booking.Slots
            .OrderBy(s => s.StartTime)
            .Select(s => new TimeSlotDto(
                s.Id.ToString(),
                s.Date.ToString("yyyy-MM-dd"),
                s.StartTime.ToString("HH:mm"),
                s.EndTime.ToString("HH:mm"),
                false,
                s.Price))
            .ToList();
        var previousDate = booking.Date;
        var previousCourtId = booking.CourtId;
        var previousCourtName = booking.Court?.Name ?? "";
        var previousAmount = booking.TotalAmount;

        // ── Recompute total using pricing rules ──────────────────
        var dateOnly = DateOnly.FromDateTime(targetDate);
        decimal newTotal = 0m;
        var perSlotPrices = new List<decimal>();

        foreach (var (start, end) in ordered)
        {
            var hours = (decimal)(end - start).TotalHours;
            var rate = _pricingRuleService.ResolvePriceFromCourt(targetCourt, dateOnly, start);
            var price = Math.Round(rate * hours, 2);
            perSlotPrices.Add(price);
            newTotal += price;
        }

        // ── Swap slots + update booking ──────────────────────────
        _db.TimeSlots.RemoveRange(booking.Slots);

        booking.Slots = ordered
            .Select((s, i) => new TimeSlot
            {
                BookingId = booking.Id,
                CourtId = targetCourtId,
                Date = targetDate,
                StartTime = s.Start,
                EndTime = s.End,
                Price = perSlotPrices[i]
            })
            .ToList();

        booking.CourtId = targetCourtId;
        booking.Court = targetCourt;
        booking.Date = targetDate;
        booking.TotalAmount = newTotal;

        var changeSummary =
            $"[RESCHEDULED {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC] " +
            $"{previousDate:yyyy-MM-dd} {previousCourtName} " +
            $"→ {targetDate:yyyy-MM-dd} {targetCourt.Name}" +
            (string.IsNullOrWhiteSpace(request.Reason) ? "" : $" · Reason: {request.Reason}") +
            (string.IsNullOrWhiteSpace(request.StaffNotes) ? "" : $" · Staff: {request.StaffNotes}");

        booking.Notes = string.IsNullOrWhiteSpace(booking.Notes)
            ? changeSummary
            : $"{booking.Notes}\n{changeSummary}";

        await _db.SaveChangesAsync();

        // ── Customer email (non-fatal) ───────────────────────────
        if (!string.IsNullOrWhiteSpace(booking.CustomerEmail))
        {
            try
            {
                await _email.NotifyCustomerBookingRescheduledAsync(
                    booking.CustomerEmail,
                    booking.CustomerName,
                    booking.ReferenceCode,
                    previousDate.ToString("yyyy-MM-dd"),
                    string.Join(", ", previousSlots.Select(s => $"{s.StartTime}-{s.EndTime}")),
                    previousCourtName,
                    targetDate.ToString("yyyy-MM-dd"),
                    string.Join(", ", ordered.Select(s => $"{s.Start:HH\\:mm}-{s.End:HH\\:mm}")),
                    targetCourt.Name,
                    $"₱{newTotal:N2}",
                    request.Reason);
            }
            catch { }
        }

        var delta = newTotal - previousAmount;
        var balanceDue = delta > 0 ? delta : 0m;
        var refundDue = delta < 0 ? -delta : 0m;

        return new RescheduleBookingResponse(
            MapToDto(booking, targetCourt.Name),
            previousSlots,
            previousDate.ToString("yyyy-MM-dd"),
            previousCourtId.ToString(),
            previousCourtName,
            previousAmount,
            newTotal,
            balanceDue,
            refundDue);
    }

    public async Task<List<BookingSummaryDto>> TrackBookingSummariesByEmailAsync(string email, Guid clientId)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Email is required.");

        var normalized = email.Trim().ToLowerInvariant();

        var bookings = await _db.Bookings
            .Where(b => b.ClientId == clientId
                     && b.CustomerEmail.ToLower() == normalized
                     && (b.Status == "pending_payment" || b.Status == "payment_submitted"))
            .Include(b => b.Court)
            .Include(b => b.Slots)
            .OrderByDescending(b => b.CreatedAt)
            .Take(20)
            .ToListAsync();

        return bookings.Select(b =>
        {
            var firstSlot = b.Slots.OrderBy(s => s.StartTime).FirstOrDefault();
            var lastSlot = b.Slots.OrderByDescending(s => s.EndTime).FirstOrDefault();

            return new BookingSummaryDto(
                b.Id.ToString(),
                b.ReferenceCode,
                b.Court?.Name ?? "",
                b.Date.ToString("yyyy-MM-dd"),
                firstSlot?.StartTime.ToString("HH:mm") ?? "",
                lastSlot?.EndTime.ToString("HH:mm") ?? "",
                b.Status,
                b.TotalAmount,
                b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            );
        }).ToList();
    }
}