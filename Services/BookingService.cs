using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Entities;
using PickleballBookingSystem.Interfaces;
using System.Text.RegularExpressions; // 👈 ADDED

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

    // 👇 ADDED: Input sanitization helper
    private static string SanitizeInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return Regex.Replace(input, "<.*?>", string.Empty).Trim();
    }

    public async Task<BookingDto> CreateBookingAsync(CreateBookingRequest request, Guid clientId)
    {
        // 👇 ADDED: Strict backend validation to block test/troll bookings
        var lowerName = request.CustomerName?.Trim().ToLower() ?? "";
        var lowerEmail = request.CustomerEmail?.Trim().ToLower() ?? "";

        var blockedWords = new[] { "test", "sample", "testing", "admin", "fake", "trap", "hack" };

        if (blockedWords.Any(w => lowerName.Contains(w) || lowerEmail.Contains(w)))
        {
            throw new InvalidOperationException("Please use a real name and email address.");
        }

        if (Regex.IsMatch(lowerName, @"^(.)\1+$"))
        {
            throw new InvalidOperationException("Please enter a valid name.");
        }

        var blockedDomains = new[] { "test.com", "example.com", "mailinator.com", "tempmail.com" };
        if (blockedDomains.Any(d => lowerEmail.EndsWith(d)))
        {
            throw new InvalidOperationException("Please use a real, active email address.");
        }

        var phone = request.CustomerPhone?.Trim() ?? "";
        if (phone == "09121451236" || phone == "09120210125" || phone == "09000000000")
        {
            throw new InvalidOperationException("Please use a real mobile number.");
        }
        // ------------------------------------

        if (!DateTime.TryParse(request.Date, out var bookingDate))
            throw new InvalidOperationException("Invalid date format");

        bookingDate = DateTime.SpecifyKind(bookingDate.Date, DateTimeKind.Utc);
        await EnsureWithinAdvanceWindowAsync(clientId, bookingDate);

        if (request.CourtGroups == null || !request.CourtGroups.Any())
            throw new InvalidOperationException("At least one court must be selected");

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
                    throw new InvalidOperationException($"Invalid court ID: {group.CourtId}");

                var court = await _db.Courts
                    .Include(c => c.PricingRules)
                    .FirstOrDefaultAsync(c => c.Id == courtGuid && c.ClientId == clientId)
                    ?? throw new KeyNotFoundException($"Court {group.CourtId} not found");

                foreach (var slot in group.Slots)
                {
                    if (!TimeOnly.TryParse(slot.StartTime, out var startTime))
                        throw new InvalidOperationException($"Invalid start time: {slot.StartTime}");
                    if (!TimeOnly.TryParse(slot.EndTime, out var endTime))
                        throw new InvalidOperationException($"Invalid end time: {slot.EndTime}");

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
                            $"Time slot {slot.StartTime}-{slot.EndTime} is already booked on {court.Name}");

                    var isBlocked = await _db.BlockedDates
                        .AnyAsync(bd => bd.CourtId == courtGuid
                            && bd.Date == bookingDate
                            && (bd.StartTime == null || bd.StartTime <= startTime)
                            && (bd.EndTime == null || bd.EndTime >= endTime));

                    if (isBlocked)
                        throw new InvalidOperationException(
                            $"Time slot {slot.StartTime}-{slot.EndTime} is blocked on {court.Name}");

                    var hours = (decimal)(endTime - startTime).TotalHours;
                    var ratePerHour = _pricingRuleService.ResolvePriceFromCourt(court, dateOnly, startTime);
                    var price = Math.Round(ratePerHour * hours, 2);

                    newSlots.Add((courtGuid, startTime, endTime, price));
                    computedTotal += price;
                }
            }

            var referenceCode = $"BK-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
            var primaryCourtId = newSlots.First().CourtId;

            var booking = new Booking
            {
                ClientId = clientId,
                CourtId = primaryCourtId,
                CustomerName = SanitizeInput(request.CustomerName).Trim(),
                CustomerEmail = SanitizeInput(request.CustomerEmail).Trim().ToLower(),
                CustomerPhone = SanitizeInput(request.CustomerPhone)?.Trim(),
                ReferenceCode = referenceCode,
                Date = bookingDate,
                TotalAmount = computedTotal,
                Status = "pending_payment",
                PaymentMethod = "gcash",
                Notes = SanitizeInput(request.Notes), // 👈 Sanitized
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

            try
            {
                var courtIds = booking.Slots.Select(s => s.CourtId).Distinct();
                var courts = await _db.Courts.Where(c => courtIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);

                var slotsText = string.Join(" | ", booking.Slots
                    .GroupBy(s => s.CourtId)
                    .Select(g => {
                        var courtName = courts.TryGetValue(g.Key, out var name) ? name : "Court";
                        var times = string.Join(", ", g.OrderBy(s => s.StartTime).Select(s => $"{s.StartTime:HH:mm}-{s.EndTime:HH:mm}"));
                        return $"{courtName}: {times}";
                    }));

                await _email.NotifyAdminNewBookingAsync(
                    booking.CustomerName,
                    booking.ReferenceCode,
                    booking.Date.ToString("yyyy-MM-dd"),
                    slotsText,
                    $"₱{booking.TotalAmount:N2}"
                );
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

    // ... (Rest of the file remains exactly the same as your previous version)
    // (GetBookingAsync, TrackBookingAsync, etc.)
}