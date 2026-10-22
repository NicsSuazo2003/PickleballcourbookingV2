using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    private const int MaxSlotHours = 6;
    private const int MaxSlotsPerBooking = 12;

    public BookingService(AppDbContext db, EmailService email, IPricingRuleService pricingRuleService)
    {
        _db = db;
        _email = email;
        _pricingRuleService = pricingRuleService;
    }

    // ── helpers ─────────────────────────────────────────────
    private static string SanitizeText(string? input, int max = 500)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var stripped = Regex.Replace(input, "<[^>]*>", string.Empty, RegexOptions.Compiled);
        stripped = stripped.Trim();
        return stripped.Length > max ? stripped[..max] : stripped;
    }

    private static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string NormalizePhone(string? phone) =>
        Regex.Replace(phone ?? string.Empty, "[^0-9]", "");

    private async Task EnsureWithinAdvanceWindowAsync(Guid clientId, DateTime bookingDate)
    {
        var client = await _db.Clients.FindAsync(clientId);
        if (client is null) return;

        if (client.MaxAdvanceBookingDays > 0)
        {
            var maxDate = DateTime.UtcNow.Date.AddDays(client.MaxAdvanceBookingDays);
            if (bookingDate.Date > maxDate)
                throw new InvalidOperationException(
                    $"Bookings can only be made up to {client.MaxAdvanceBookingDays} days in advance.");
        }

        if (bookingDate.Date < DateTime.UtcNow.Date)
            throw new InvalidOperationException("Cannot book in the past.");
    }

    // ═══════════════════════════════════════════════════════
    // CREATE
    // ═══════════════════════════════════════════════════════
    public async Task<BookingDto> CreateBookingAsync(CreateBookingRequest request, Guid clientId)
    {
        if (!request.AdminOverride)
        {
            var bookingClient = await _db.Clients.FindAsync(clientId)
                ?? throw new KeyNotFoundException("Client not found");

            if (!bookingClient.BookingsEnabled)
                throw new InvalidOperationException(
                    "Online bookings are temporarily closed. Please contact the court directly.");
        }

        // ── field-level checks ──
        var name = SanitizeText(request.CustomerName, 80);
        var email = NormalizeEmail(request.CustomerEmail);
        var phone = NormalizePhone(request.CustomerPhone);

        if (name.Length < 2)
            throw new InvalidOperationException("Please enter your real full name.");
        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw new InvalidOperationException("Please use a valid email address.");
        if (phone.Length is < 10 or > 13)
            throw new InvalidOperationException("Please use a valid mobile number.");

        if (!DateTime.TryParse(request.Date, out var bookingDate))
            throw new InvalidOperationException("Invalid date format");

        bookingDate = DateTime.SpecifyKind(bookingDate.Date, DateTimeKind.Utc);
        await EnsureWithinAdvanceWindowAsync(clientId, bookingDate);

        if (request.CourtGroups == null || request.CourtGroups.Count == 0)
            throw new InvalidOperationException("At least one court must be selected");

        var totalSlotCount = request.CourtGroups.Sum(g => g.Slots?.Count ?? 0);
        if (totalSlotCount is 0 or > MaxSlotsPerBooking)
            throw new InvalidOperationException($"A booking may contain 1–{MaxSlotsPerBooking} slots.");

        // 🔒 One active pending booking per email
        var now = DateTime.UtcNow;
        var hasPending = await _db.Bookings.AnyAsync(b =>
            b.ClientId == clientId &&
            b.CustomerEmail.ToLower() == email &&
            b.Status == "pending_payment" &&
            b.PaymentExpiresAt > now);
        if (hasPending && !request.AdminOverride)
            throw new InvalidOperationException(
                "You already have a booking awaiting payment. Please complete or cancel it before creating a new one.");

        // Serialization retry (Postgres 40001)
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await CreateInnerAsync(request, clientId, bookingDate, name, email, phone);
            }
            catch (DbUpdateException ex) when (IsSerializationFailure(ex) && attempt < 2)
            {
                await Task.Delay(50 * (attempt + 1));
            }
        }
    }

    private static bool IsSerializationFailure(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == "40001";

    private async Task<BookingDto> CreateInnerAsync(
        CreateBookingRequest request,
        Guid clientId,
        DateTime bookingDate,
        string name,
        string email,
        string phone)
    {
        using var transaction = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        try
        {
            var newSlots = new List<(Guid CourtId, TimeOnly Start, TimeOnly End, decimal Price)>();
            decimal computedTotal = 0m;
            var dateOnly = DateOnly.FromDateTime(bookingDate);

            foreach (var group in request.CourtGroups)
            {
                if (!Guid.TryParse(group.CourtId, out var courtGuid))
                    throw new InvalidOperationException("Invalid court identifier.");

                var court = await _db.Courts
                    .Include(c => c.PricingRules)
                    .FirstOrDefaultAsync(c => c.Id == courtGuid && c.ClientId == clientId)
                    ?? throw new KeyNotFoundException("Court not found for this client.");

                foreach (var slot in group.Slots)
                {
                    if (!TimeOnly.TryParse(slot.StartTime, out var startTime))
                        throw new InvalidOperationException("Invalid start time.");
                    if (!TimeOnly.TryParse(slot.EndTime, out var endTime))
                        throw new InvalidOperationException("Invalid end time.");
                    if (endTime <= startTime)
                        throw new InvalidOperationException("End time must be after start time.");

                    var hours = (decimal)(endTime - startTime).TotalHours;
                    if (hours > MaxSlotHours)
                        throw new InvalidOperationException($"A single slot cannot exceed {MaxSlotHours} hours.");

                    var conflicting = await _db.TimeSlots
                        .Where(s => s.CourtId == courtGuid
                                 && s.Date == bookingDate
                                 && s.StartTime < endTime
                                 && s.EndTime > startTime
                                 && s.Booking.Status != "cancelled"
                                 && s.Booking.Status != "expired"
                                 && s.Booking.Status != "rejected"
                                 && s.Booking.Status != "refunded")
                        .AnyAsync();

                    if (conflicting)
                        throw new InvalidOperationException(
                            "One of your selected time slots was just taken. Please pick a different time.");

                    var isBlocked = await _db.BlockedDates
                        .AnyAsync(bd => bd.CourtId == courtGuid
                            && bd.Date == bookingDate
                            && (bd.StartTime == null || bd.StartTime <= startTime)
                            && (bd.EndTime == null || bd.EndTime >= endTime));

                    if (isBlocked)
                        throw new InvalidOperationException(
                            "One of your selected time slots is blocked on this court.");

                    var ratePerHour = _pricingRuleService.ResolvePriceFromCourt(court, dateOnly, startTime);
                    var price = Math.Round(ratePerHour * hours, 2);
                    if (price < 0m)
                        throw new InvalidOperationException("Invalid slot price.");

                    newSlots.Add((courtGuid, startTime, endTime, price));
                    computedTotal += price;
                }
            }

            // In-request overlap check (per court)
            foreach (var courtGrp in newSlots.GroupBy(s => s.CourtId))
            {
                var ordered = courtGrp.OrderBy(s => s.Start).ToList();
                for (int i = 1; i < ordered.Count; i++)
                {
                    if (ordered[i].Start < ordered[i - 1].End)
                        throw new InvalidOperationException(
                            "Selected time slots overlap each other. Please pick non-overlapping slots.");
                }
            }

            var referenceCode = ReferenceCodeGenerator.Generate("BK");
            var primaryCourtId = newSlots[0].CourtId;

            var booking = new Booking
            {
                ClientId = clientId,
                CourtId = primaryCourtId,
                CustomerName = name,
                CustomerEmail = email,
                CustomerPhone = phone,
                ReferenceCode = referenceCode,
                Date = bookingDate,
                TotalAmount = computedTotal,
                Status = "pending_payment",
                PaymentMethod = "gcash",
                Notes = SanitizeText(request.Notes),
                CreatedAt = DateTime.UtcNow,
                PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15),
                Slots = newSlots.Select(s => new TimeSlot
                {
                    CourtId = s.CourtId,
                    Date = bookingDate,
                    StartTime = s.Start,
                    EndTime = s.End,
                    Price = s.Price
                }).ToList()
            };

            _db.Bookings.Add(booking);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // Fire-and-forget admin notification
            try
            {
                var courtIds = booking.Slots.Select(s => s.CourtId!.Value).Distinct().ToList();
                var courts = await _db.Courts
                    .Where(c => courtIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, c => c.Name);

                var slotsText = string.Join(" | ", booking.Slots
                    .GroupBy(s => s.CourtId!.Value)
                    .Select(g =>
                    {
                        var courtName = courts.TryGetValue(g.Key, out var n) ? n : "Court";
                        var times = string.Join(", ", g.OrderBy(s => s.StartTime)
                            .Select(s => $"{s.StartTime:HH:mm}-{s.EndTime:HH:mm}"));
                        return $"{courtName}: {times}";
                    }));

                await _email.NotifyAdminNewBookingAsync(
                    booking.CustomerName,
                    booking.ReferenceCode,
                    booking.Date.ToString("yyyy-MM-dd"),
                    slotsText,
                    $"₱{booking.TotalAmount:N2}");
            }
            catch { }

            var primaryCourtName = (await _db.Courts.FindAsync(primaryCourtId))?.Name ?? "Multiple Courts";
            return MapToDto(booking, primaryCourtName);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ═══════════════════════════════════════════════════════
    // READ
    // ═══════════════════════════════════════════════════════
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
        if (string.IsNullOrWhiteSpace(referenceCode))
            throw new InvalidOperationException("Reference code is required.");

        // 🔒 Require email — prevents reference-code enumeration
        if (string.IsNullOrWhiteSpace(email))
            throw new UnauthorizedAccessException("Email is required to look up a booking.");

        var normalizedEmail = NormalizeEmail(email);
        var normalizedRef = referenceCode.Trim();

        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b =>
                b.ReferenceCode == normalizedRef &&
                b.ClientId == clientId &&
                b.CustomerEmail.ToLower() == normalizedEmail);

        if (booking is null)
            throw new KeyNotFoundException("Booking not found");

        return MapToDto(booking, booking.Court?.Name ?? "");
    }

    public async Task<List<BookingDto>> GetBookingsAsync(
        Guid clientId, DateTime? fromDate = null, DateTime? toDate = null)
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
            .Take(500)
            .ToListAsync();

        return bookings.Select(b => MapToDto(b, b.Court?.Name ?? "")).ToList();
    }

    public Task<List<BookingDto>> GetAllBookingsAsync(Guid clientId) =>
        GetBookingsAsync(clientId);

    // ═══════════════════════════════════════════════════════
    // UPDATE STATUS
    // ═══════════════════════════════════════════════════════
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

    public Task<BookingDto> AdminUpdateBookingAsync(Guid id, AdminUpdateBookingRequest request, Guid clientId) =>
        UpdateBookingStatusAsync(id, request.Status, clientId);

    // ═══════════════════════════════════════════════════════
    // PAYMENT
    // ═══════════════════════════════════════════════════════
    public async Task<BookingDto> UploadPaymentAsync(
        Guid id, string screenshotBase64, string? paymentReference, Guid clientId)
    {
        // Deprecated path — delegate to the URL-based one with a data-url.
        return await UploadPaymentScreenshotAsync(
            id,
            string.IsNullOrEmpty(screenshotBase64) ? null : screenshotBase64,
            paymentReference,
            null,
            clientId);
    }

    public async Task<BookingDto> UploadPaymentScreenshotAsync(
        Guid id, string? screenshotUrl, string? paymentReference, string? paymentMethod, Guid clientId)
    {
        if (string.IsNullOrWhiteSpace(paymentReference) || paymentReference.Trim().Length < 4)
            throw new InvalidOperationException("A valid payment reference is required.");

        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        if (booking.Status != "pending_payment")
            throw new InvalidOperationException("Booking is not pending payment");

        if (booking.PaymentExpiresAt is { } exp && exp < DateTime.UtcNow)
            throw new InvalidOperationException("Payment window has expired. Please start a new booking.");

        if (string.IsNullOrEmpty(screenshotUrl) && paymentReference.Trim().Length < 8)
            throw new InvalidOperationException(
                "Please attach a screenshot or enter the full payment reference number.");

        if (!string.IsNullOrEmpty(booking.PaymentReference) &&
            booking.PaymentReference != paymentReference.Trim())
            throw new InvalidOperationException("A payment has already been submitted for this booking.");

        if (!string.IsNullOrEmpty(screenshotUrl))
            booking.PaymentScreenshot = screenshotUrl;

        booking.PaymentReference = paymentReference.Trim();
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

        var prev = booking.Status;
        booking.Status = "confirmed";
        await _db.SaveChangesAsync();
        await SendStatusChangeEmailAsync(booking, prev);
    }

    public async Task CancelBookingAsync(Guid id, Guid clientId)
    {
        var booking = await _db.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        booking.Status = "cancelled";
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════
    // WORKERS
    // ═══════════════════════════════════════════════════════
    public async Task AutoCompletePastBookingsAsync(Guid clientId)
    {
        var past = await _db.Bookings
            .Where(b => b.ClientId == clientId
                && b.Date < DateTime.UtcNow.Date
                && b.Status != "completed"
                && b.Status != "cancelled"
                && b.Status != "rejected"
                && b.Status != "refunded"
                && b.Status != "expired")
            .Take(500)
            .ToListAsync();

        foreach (var b in past) b.Status = "completed";
        await _db.SaveChangesAsync();
    }

    public async Task CancelExpiredPaymentsAsync(Guid clientId)
    {
        var expired = await _db.Bookings
            .Where(b => b.ClientId == clientId
                && b.Status == "pending_payment"
                && b.PaymentExpiresAt != null
                && b.PaymentExpiresAt < DateTime.UtcNow)
            .Take(500)
            .ToListAsync();

        foreach (var b in expired) b.Status = "expired";
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════
    // EMAIL
    // ═══════════════════════════════════════════════════════
    private async Task SendStatusChangeEmailAsync(Booking booking, string previousStatus)
    {
        if (previousStatus == booking.Status) return;
        if (string.IsNullOrWhiteSpace(booking.CustomerEmail)) return;

        var timeRange = string.Join(", ", booking.Slots
            .OrderBy(s => s.StartTime)
            .Select(s => $"{s.StartTime:HH:mm}-{s.EndTime:HH:mm}"));
        var dateStr = booking.Date.ToString("yyyy-MM-dd");
        var amountStr = $"₱{booking.TotalAmount:N2}";

        try
        {
            switch (booking.Status)
            {
                case "confirmed":
                    await _email.NotifyCustomerBookingConfirmedAsync(
                        booking.CustomerEmail, booking.CustomerName, booking.ReferenceCode,
                        dateStr, timeRange, amountStr);
                    break;
                case "rejected":
                    await _email.NotifyCustomerBookingRejectedAsync(
                        booking.CustomerEmail, booking.CustomerName, booking.ReferenceCode,
                        dateStr, timeRange, null, amountStr);
                    break;
                case "cancelled":
                    await _email.NotifyCustomerBookingCancelledAsync(
                        booking.CustomerEmail, booking.CustomerName, booking.ReferenceCode,
                        dateStr, timeRange, null, amountStr);
                    break;
                case "refunded" when booking.TotalAmount > 0:
                    await _email.NotifyCustomerBookingRefundedAsync(
                        booking.CustomerEmail, booking.CustomerName, booking.ReferenceCode,
                        dateStr, timeRange, amountStr);
                    break;
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════
    // STAFF CREATE
    // ═══════════════════════════════════════════════════════
    public async Task<BookingDto> CreateStaffBookingAsync(StaffCreateBookingRequest request, Guid clientId)
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

        if (request.Slots == null || request.Slots.Count == 0)
            throw new InvalidOperationException("At least one time slot is required");
        if (request.Slots.Count > MaxSlotsPerBooking)
            throw new InvalidOperationException($"Max {MaxSlotsPerBooking} slots per booking.");

        foreach (var slot in request.Slots)
        {
            if (!TimeOnly.TryParse(slot.StartTime, out var st))
                throw new InvalidOperationException("Invalid start time.");
            if (!TimeOnly.TryParse(slot.EndTime, out var en))
                throw new InvalidOperationException("Invalid end time.");
            if (en <= st)
                throw new InvalidOperationException("End time must be after start time.");

            var conflicting = await _db.Bookings
                .Where(b => b.CourtId == courtGuid
                    && b.Date == bookingDate
                    && b.Status != "cancelled"
                    && b.Status != "expired"
                    && b.Status != "rejected"
                    && b.Status != "refunded")
                .SelectMany(b => b.Slots)
                .Where(s => s.Date == bookingDate && s.StartTime < en && s.EndTime > st)
                .AnyAsync();

            if (conflicting)
                throw new InvalidOperationException("One or more slots are already booked.");
        }

        var mode = (request.PaymentMode ?? "cash").ToLowerInvariant();
        string status, paymentMethod;
        DateTime? expiresAt;
        switch (mode)
        {
            case "gcash":
                status = "pending_payment"; paymentMethod = "gcash";
                expiresAt = DateTime.UtcNow.AddMinutes(15); break;
            case "pay_later":
                status = "pending_payment"; paymentMethod = "cash"; expiresAt = null; break;
            case "free":
            case "comp":
                status = "confirmed"; paymentMethod = "comp"; expiresAt = null; break;
            case "cash":
            default:
                status = "confirmed"; paymentMethod = "cash"; expiresAt = null; break;
        }

        // Compute from pricing rules; ignore client-supplied amount unless it came from an admin
        var courtWithRules = await _db.Courts.Include(c => c.PricingRules)
            .FirstAsync(c => c.Id == courtGuid);
        var dateOnly = DateOnly.FromDateTime(bookingDate);
        decimal computed = 0m;
        foreach (var s in request.Slots)
        {
            var st = TimeOnly.Parse(s.StartTime);
            var en = TimeOnly.Parse(s.EndTime);
            computed += _pricingRuleService.ResolvePriceFromCourt(courtWithRules, dateOnly, st)
                        * (decimal)(en - st).TotalHours;
        }
        computed = Math.Round(computed, 2);

        var totalAmount = (request.TotalAmount.HasValue && request.TotalAmount.Value >= 0)
            ? request.TotalAmount.Value
            : computed;

        var booking = new Booking
        {
            ClientId = clientId,
            CourtId = courtGuid,
            CustomerName = SanitizeText(request.CustomerName, 80),
            CustomerEmail = NormalizeEmail(request.CustomerEmail),
            CustomerPhone = NormalizePhone(request.CustomerPhone),
            ReferenceCode = ReferenceCodeGenerator.Generate("ST"),
            Date = bookingDate,
            TotalAmount = totalAmount,
            Status = status,
            PaymentMethod = paymentMethod,
            Notes = SanitizeText(request.Notes),
            CreatedAt = DateTime.UtcNow,
            PaymentExpiresAt = expiresAt,
            Slots = request.Slots.Select(s => new TimeSlot
            {
                CourtId = courtGuid,
                Date = bookingDate,
                StartTime = TimeOnly.Parse(s.StartTime),
                EndTime = TimeOnly.Parse(s.EndTime),
                Price = Math.Round(computed / request.Slots.Count, 2)
            }).ToList()
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        if (request.SendConfirmation && !string.IsNullOrWhiteSpace(booking.CustomerEmail))
        {
            try
            {
                await _email.NotifyCustomerBookingConfirmedAsync(
                    booking.CustomerEmail, booking.CustomerName, booking.ReferenceCode,
                    booking.Date.ToString("yyyy-MM-dd"),
                    string.Join(", ", booking.Slots.Select(s => $"{s.StartTime:HH:mm} - {s.EndTime:HH:mm}")),
                    $"₱{booking.TotalAmount:N2}");
            }
            catch { }
        }

        return MapToDto(booking, court.Name);
    }

    // ═══════════════════════════════════════════════════════
    // RESCHEDULE
    // ═══════════════════════════════════════════════════════
    public async Task<RescheduleBookingResponse> RescheduleBookingAsync(
        Guid id, RescheduleBookingRequest request, Guid clientId)
    {
        if (request.Slots == null || request.Slots.Count == 0)
            throw new InvalidOperationException("At least one time slot is required");

        var booking = await _db.Bookings
            .Include(b => b.Slots)
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Booking not found");

        if (booking.Status is "cancelled" or "rejected" or "expired" or "refunded")
            throw new InvalidOperationException($"Cannot reschedule a booking that is already {booking.Status}");

        var targetCourtId = booking.CourtId;
        if (!string.IsNullOrWhiteSpace(request.CourtId))
        {
            if (!Guid.TryParse(request.CourtId, out var parsed)) throw new InvalidOperationException("Invalid court ID.");
            targetCourtId = parsed;
        }

        var targetCourt = await _db.Courts.Include(c => c.PricingRules)
            .FirstOrDefaultAsync(c => c.Id == targetCourtId && c.ClientId == clientId)
            ?? throw new KeyNotFoundException("Target court not found");

        var targetDate = booking.Date;
        if (!string.IsNullOrWhiteSpace(request.Date))
        {
            if (!DateTime.TryParse(request.Date, out var parsed))
                throw new InvalidOperationException("Invalid date format");
            targetDate = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
        }
        await EnsureWithinAdvanceWindowAsync(clientId, targetDate);

        var newSlots = new List<(TimeOnly Start, TimeOnly End)>();
        foreach (var s in request.Slots)
        {
            if (!TimeOnly.TryParse(s.StartTime, out var st)) throw new InvalidOperationException("Invalid start time.");
            if (!TimeOnly.TryParse(s.EndTime, out var en)) throw new InvalidOperationException("Invalid end time.");
            if (en <= st) throw new InvalidOperationException("Slot end must be after start.");
            newSlots.Add((st, en));
        }

        var ordered = newSlots.OrderBy(s => s.Start).ToList();
        for (int i = 1; i < ordered.Count; i++)
            if (ordered[i].Start < ordered[i - 1].End)
                throw new InvalidOperationException("New slots overlap each other");

        var openHour = targetCourt.OpenTime.Hour;
        var closeHour = targetCourt.CloseTime.Hour == 0 ? 24 : targetCourt.CloseTime.Hour;
        foreach (var (start, end) in ordered)
        {
            var endHour = end.Hour == 0 ? 24 : end.Hour;
            if (start.Hour < openHour || endHour > closeHour)
                throw new InvalidOperationException(
                    $"{start:HH\\:mm}-{end:HH\\:mm} is outside this court's operating hours");
        }

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
                .Where(s => s.Date == targetDate && s.StartTime < end && s.EndTime > start)
                .AnyAsync();
            if (conflicting)
                throw new InvalidOperationException("A requested slot is already booked.");
        }

        var previousCourtId = booking.CourtId;
        var previousCourtName = booking.Court?.Name ?? "";
        var previousDate = booking.Date;
        var previousAmount = booking.TotalAmount;
        var previousSlots = booking.Slots.OrderBy(s => s.StartTime).Select(s => new TimeSlotDto(
            s.Id.ToString(),
            (s.CourtId ?? previousCourtId).ToString(),
            s.Date.ToString("yyyy-MM-dd"),
            s.StartTime.ToString("HH:mm"),
            s.EndTime.ToString("HH:mm"),
            false,
            s.Price)).ToList();

        var dateOnly = DateOnly.FromDateTime(targetDate);
        decimal newTotal = 0m;
        var perSlotPrices = new List<decimal>();
        foreach (var (start, end) in ordered)
        {
            var rate = _pricingRuleService.ResolvePriceFromCourt(targetCourt, dateOnly, start);
            var price = Math.Round(rate * (decimal)(end - start).TotalHours, 2);
            perSlotPrices.Add(price);
            newTotal += price;
        }

        _db.TimeSlots.RemoveRange(booking.Slots);
        booking.Slots = ordered.Select((s, i) => new TimeSlot
        {
            BookingId = booking.Id,
            CourtId = targetCourtId,
            Date = targetDate,
            StartTime = s.Start,
            EndTime = s.End,
            Price = perSlotPrices[i]
        }).ToList();

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

        if (!string.IsNullOrWhiteSpace(booking.CustomerEmail))
        {
            try
            {
                await _email.NotifyCustomerBookingRescheduledAsync(
                    booking.CustomerEmail, booking.CustomerName, booking.ReferenceCode,
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
        return new RescheduleBookingResponse(
            MapToDto(booking, targetCourt.Name),
            previousSlots,
            previousDate.ToString("yyyy-MM-dd"),
            previousCourtId.ToString(),
            previousCourtName,
            previousAmount,
            newTotal,
            delta > 0 ? delta : 0m,
            delta < 0 ? -delta : 0m);
    }

    // ═══════════════════════════════════════════════════════
    // SUMMARY BY EMAIL
    // ═══════════════════════════════════════════════════════
    public async Task<List<BookingSummaryDto>> TrackBookingSummariesByEmailAsync(string email, Guid clientId)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Email is required.");

        var normalized = NormalizeEmail(email);

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
            var first = b.Slots.OrderBy(s => s.StartTime).FirstOrDefault();
            var last = b.Slots.OrderByDescending(s => s.EndTime).FirstOrDefault();
            return new BookingSummaryDto(
                b.Id.ToString(),
                b.ReferenceCode,
                b.Court?.Name ?? "",
                b.Date.ToString("yyyy-MM-dd"),
                first?.StartTime.ToString("HH:mm") ?? "",
                last?.EndTime.ToString("HH:mm") ?? "",
                b.Status,
                b.TotalAmount,
                b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        }).ToList();
    }

    // ═══════════════════════════════════════════════════════
    // MAPPER
    // ═══════════════════════════════════════════════════════
    private static BookingDto MapToDto(Booking b, string courtName) => new(
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
            (s.CourtId ?? b.CourtId).ToString(),
            s.Date.ToString("yyyy-MM-dd"),
            s.StartTime.ToString("HH:mm"),
            s.EndTime.ToString("HH:mm"),
            false,
            s.Price)).ToList(),
        b.TotalAmount,
        b.Status,
        b.PaymentMethod,
        b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        b.Notes,
        b.PaymentScreenshot,
        b.PaymentExpiresAt,
        b.PaymentReference);
}