using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Entities;
using PickleballBookingSystem.Interfaces;

namespace PickleballBookingSystem.Services;

public class CourtService : ICourtService
{
    private readonly AppDbContext _db;
    private readonly IPricingRuleService _pricingRuleService;
    private readonly ILogger<CourtService> _logger;

    private const int MaxAmenities = 30;
    private const int MaxAmenityLength = 60;
    private const int MaxImages = 10;
    private const int MaxDescriptionLength = 2000;
    private const int MaxNameLength = 80;
    private const decimal MaxPrice = 100_000m;

    public CourtService(
        AppDbContext db,
        IPricingRuleService pricingRuleService,
        ILogger<CourtService> logger)
    {
        _db = db;
        _pricingRuleService = pricingRuleService;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════
    // COURTS
    // ═══════════════════════════════════════════════════════

    public async Task<List<CourtDto>> GetAllCourtsAsync(Guid clientId)
    {
        return await _db.Courts
            .Where(c => c.ClientId == clientId && c.Status != "archived")
            .OrderBy(c => c.CreatedAt)
            .Select(c => MapToDto(c))
            .ToListAsync();
    }

    public async Task<CourtDto> GetCourtByIdAsync(Guid id, Guid clientId)
    {
        var court = await _db.Courts
            .FirstOrDefaultAsync(c => c.Id == id && c.ClientId == clientId)
            ?? throw new KeyNotFoundException("Court not found");
        return MapToDto(court);
    }

    public async Task<CourtDto> CreateCourtAsync(CreateCourtRequest request, Guid clientId)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > MaxNameLength)
            throw new InvalidOperationException("Invalid court name.");

        if (!TimeOnly.TryParse(request.OpenTime, out var open) ||
            !TimeOnly.TryParse(request.CloseTime, out var close))
            throw new InvalidOperationException("Invalid operating hours.");

        if (close == open)
            throw new InvalidOperationException("Close time must differ from open time.");
        if (close < open && close != TimeOnly.MinValue)
            throw new InvalidOperationException(
                "Close time must be after open time (or 00:00 for midnight).");

        ValidatePrice(request.PricePerHour, "hourly price");
        ValidatePrice(request.PeakPricePerHour, "peak price");

        var amenities = ValidateAmenities(request.Amenities);

        var court = new Court
        {
            Name = request.Name.Trim(),
            Type = request.Type,
            Indoor = request.Indoor,
            PricePerHour = request.PricePerHour,
            PeakPricePerHour = request.PeakPricePerHour,
            Amenities = amenities,
            OpenTime = open,
            CloseTime = close,
            Dimensions = request.Dimensions ?? "",
            Surface = request.Surface ?? "",
            Status = "active",
            Rating = 0,
            ClientId = clientId
        };

        _db.Courts.Add(court);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Court {CourtId} created for client {ClientId}", court.Id, clientId);
        return MapToDto(court);
    }

    public async Task<CourtDto> UpdateCourtAsync(Guid id, UpdateCourtRequest request, Guid clientId)
    {
        var court = await _db.Courts
            .FirstOrDefaultAsync(c => c.Id == id && c.ClientId == clientId)
            ?? throw new KeyNotFoundException("Court not found");

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > MaxNameLength)
                throw new InvalidOperationException("Invalid court name.");
            court.Name = request.Name.Trim();
        }

        if (request.Type is not null) court.Type = request.Type;
        if (request.Indoor.HasValue) court.Indoor = request.Indoor.Value;

        if (request.PricePerHour.HasValue)
        {
            ValidatePrice(request.PricePerHour.Value, "hourly price");
            court.PricePerHour = request.PricePerHour.Value;
        }

        if (request.PeakPricePerHour.HasValue)
        {
            ValidatePrice(request.PeakPricePerHour.Value, "peak price");
            court.PeakPricePerHour = request.PeakPricePerHour.Value;
        }

        if (request.Description is not null)
        {
            if (request.Description.Length > MaxDescriptionLength)
                throw new InvalidOperationException("Description too long.");
            court.Description = request.Description;
        }

        if (request.Amenities is not null)
            court.Amenities = ValidateAmenities(request.Amenities);

        if (request.ImageUrl is not null)
        {
            ValidateHttpsUrl(request.ImageUrl, "Image URL");
            court.ImageUrl = request.ImageUrl;
        }

        if (request.Images is not null)
        {
            if (request.Images.Count > MaxImages)
                throw new InvalidOperationException($"At most {MaxImages} images are allowed.");
            foreach (var img in request.Images)
                ValidateHttpsUrl(img, "Image URL");
            court.Images = request.Images;
        }

        if (request.Status is not null)
        {
            var allowed = new[] { "active", "archived", "maintenance" };
            if (!allowed.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid status.");
            court.Status = request.Status.ToLowerInvariant();
        }

        if (request.OpenTime is not null)
        {
            if (!TimeOnly.TryParse(request.OpenTime, out var o))
                throw new InvalidOperationException("Invalid open time.");
            court.OpenTime = o;
        }

        if (request.CloseTime is not null)
        {
            if (!TimeOnly.TryParse(request.CloseTime, out var c))
                throw new InvalidOperationException("Invalid close time.");
            court.CloseTime = c;
        }

        if (court.OpenTime == court.CloseTime)
            throw new InvalidOperationException("Open and close times must differ.");

        if (request.Dimensions is not null) court.Dimensions = request.Dimensions;
        if (request.Surface is not null) court.Surface = request.Surface;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Court {CourtId} updated for client {ClientId}", id, clientId);
        return MapToDto(court);
    }

    public async Task DeleteCourtAsync(Guid id, Guid clientId)
    {
        var court = await _db.Courts
            .FirstOrDefaultAsync(c => c.Id == id && c.ClientId == clientId)
            ?? throw new KeyNotFoundException("Court not found");

        // Refuse hard-delete when future bookings exist
        var hasFuture = await _db.Bookings
            .AnyAsync(b => b.CourtId == id
                        && b.Date >= DateTime.UtcNow.Date
                        && b.Status != "cancelled"
                        && b.Status != "expired"
                        && b.Status != "rejected"
                        && b.Status != "refunded");

        if (hasFuture)
            throw new InvalidOperationException(
                "This court has future bookings. Cancel them first, " +
                "or set the court status to 'archived' to hide it from customers.");

        // Soft delete
        court.Status = "archived";
        _logger.LogWarning("Court {CourtId} archived on tenant {ClientId}", id, clientId);
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════
    // AVAILABILITY
    // ═══════════════════════════════════════════════════════

    public async Task<List<TimeSlotAvailabilityDto>> GetCourtAvailabilityAsync(
        Guid courtId,
        DateTime date,
        Guid clientId,
        Guid? excludeBookingId = null)
    {
        date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        var court = await _db.Courts
            .Include(c => c.PricingRules)
            .FirstOrDefaultAsync(c => c.Id == courtId && c.ClientId == clientId)
            ?? throw new KeyNotFoundException("Court not found");

        var openHour = court.OpenTime.Hour;
        var closeHour = court.CloseTime.Hour;
        if (closeHour == 0) closeHour = 24;

        var excludeId = excludeBookingId ?? Guid.Empty;

        var bookedSlots = await _db.TimeSlots
            .Where(s => s.Date.Date == date.Date
                     && s.CourtId == courtId
                     && s.Booking.ClientId == clientId
                     && s.Booking.Id != excludeId
                     && s.Booking.Status != "cancelled"
                     && s.Booking.Status != "expired"
                     && s.Booking.Status != "rejected"
                     && s.Booking.Status != "refunded")
            .Select(s => new
            {
                s.StartTime,
                s.EndTime,
                s.Booking.Status,
                s.Booking.PaymentExpiresAt
            })
            .ToListAsync();

        var bookedSet = new HashSet<int>();
        var pendingExpiryByHour = new Dictionary<int, DateTime?>();

        foreach (var bs in bookedSlots)
        {
            var startH = bs.StartTime.Hour;
            var endH = bs.EndTime.Hour == 0 ? 24 : bs.EndTime.Hour;
            for (int h = startH; h < endH; h++)
            {
                bookedSet.Add(h);
                if (bs.Status == "pending_payment")
                {
                    if (!pendingExpiryByHour.TryGetValue(h, out var existing) ||
                        (bs.PaymentExpiresAt.HasValue &&
                         (!existing.HasValue || bs.PaymentExpiresAt < existing)))
                    {
                        pendingExpiryByHour[h] = bs.PaymentExpiresAt;
                    }
                }
            }
        }

        var blockedDates = await _db.BlockedDates
            .Where(b => b.Date.Date == date.Date
                     && (b.CourtId == null || b.CourtId == courtId)
                     && b.ClientId == clientId)
            .ToListAsync();

        var openPlayWindows = await _db.OpenPlaySessions
            .Where(s => s.IsActive
                     && s.ClientId == clientId
                     && s.Date.Date == date.Date
                     && s.SessionCourts.Any(sc => sc.CourtId == courtId))
            .Select(s => new { s.StartTime, s.EndTime })
            .ToListAsync();

        var blockedSet = new HashSet<int>();
        foreach (var bd in blockedDates)
        {
            if (bd.StartTime == null)
            {
                for (int h = openHour; h < closeHour; h++) blockedSet.Add(h);
            }
            else
            {
                var startH = bd.StartTime.Value.Hour;
                var endH = bd.EndTime?.Hour ?? closeHour;
                if (endH == 0) endH = 24;
                for (int h = startH; h < endH; h++) blockedSet.Add(h);
            }
        }

        var phTime = DateTime.UtcNow.AddHours(8);
        var isToday = date.Date == phTime.Date;
        var dateOnly = DateOnly.FromDateTime(date);

        var slots = new List<TimeSlotAvailabilityDto>();

        for (int h = openHour; h < closeHour; h++)
        {
            var slotTime = new TimeOnly(h % 24, 0);
            var startTime = $"{h % 24:D2}:00";
            var endTime = $"{(h + 1) % 24:D2}:00";

            var isPast = isToday && (h < phTime.Hour || (h == phTime.Hour && phTime.Minute > 0));
            var isBooked = bookedSet.Contains(h);
            var isBlocked = blockedSet.Contains(h);

            var isOpenPlayBlocked = openPlayWindows.Any(w =>
                w.StartTime.Hour <= h && h < (w.EndTime.Hour == 0 ? 24 : w.EndTime.Hour));

            var slotPrice = _pricingRuleService.ResolvePriceFromCourt(court, dateOnly, slotTime);
            var isPeak = slotPrice > court.PricePerHour;

            string status;
            DateTime? pendingExpiresAt = null;

            if (isPast) status = "past";
            else if (isOpenPlayBlocked) status = "open_play";
            else if (isBlocked) status = "blocked";
            else if (isBooked)
            {
                if (pendingExpiryByHour.TryGetValue(h, out var exp) && exp.HasValue)
                {
                    status = "pending";
                    pendingExpiresAt = exp;
                }
                else status = "booked";
            }
            else status = "available";

            slots.Add(new TimeSlotAvailabilityDto(
                $"slot-{courtId}-{date:yyyy-MM-dd}-{h}",
                date.ToString("yyyy-MM-dd"),
                startTime, endTime,
                status == "available",
                slotPrice,
                isPeak,
                status,
                pendingExpiresAt));
        }

        return slots;
    }

    // ═══════════════════════════════════════════════════════
    // BLOCKED DATES
    // ═══════════════════════════════════════════════════════

    public async Task<List<BlockedDateDto>> GetBlockedDatesAsync(Guid? courtId, Guid clientId)
    {
        var query = _db.BlockedDates.Where(b => b.ClientId == clientId);
        if (courtId.HasValue)
            query = query.Where(b => b.CourtId == courtId || b.CourtId == null);

        return await query
            .OrderBy(b => b.Date)
            .Take(500)
            .Select(b => new BlockedDateDto(
                b.Id.ToString(),
                b.Date.ToString("yyyy-MM-dd"),
                b.StartTime.HasValue ? b.StartTime.Value.ToString("HH:mm") : null,
                b.EndTime.HasValue ? b.EndTime.Value.ToString("HH:mm") : null,
                b.Reason))
            .ToListAsync();
    }

    public async Task<BlockedDateDto> AddBlockedDateAsync(
        CreateBlockedDateRequest request, Guid? courtId, Guid clientId)
    {
        if (clientId == Guid.Empty)
            throw new ArgumentException("Client identification required.");

        if (!DateTime.TryParse(request.Date, out var parsedDate))
            throw new ArgumentException("Invalid date format.");

        // Court must belong to this tenant
        if (courtId.HasValue)
        {
            var courtExists = await _db.Courts
                .AnyAsync(c => c.Id == courtId.Value && c.ClientId == clientId);
            if (!courtExists)
                throw new KeyNotFoundException("Court not found for this client.");
        }

        TimeOnly? startTime = null;
        TimeOnly? endTime = null;

        var hasStart = !string.IsNullOrEmpty(request.StartTime);
        var hasEnd = !string.IsNullOrEmpty(request.EndTime);

        if (hasStart != hasEnd)
            throw new ArgumentException("Provide both start and end time, or neither for a full-day block.");

        if (hasStart && hasEnd)
        {
            if (!TimeOnly.TryParse(request.StartTime, out var st) ||
                !TimeOnly.TryParse(request.EndTime, out var en))
                throw new ArgumentException("Invalid time format.");

            if (en != TimeOnly.MinValue && en <= st)
                throw new ArgumentException("End time must be after start time.");

            startTime = st;
            endTime = en;
        }

        var normalizedDate = DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Utc);

        // Duplicate check
        var duplicate = await _db.BlockedDates.AnyAsync(bd =>
            bd.ClientId == clientId
            && bd.CourtId == courtId
            && bd.Date == normalizedDate
            && bd.StartTime == startTime
            && bd.EndTime == endTime);

        if (duplicate)
            throw new InvalidOperationException("An identical block already exists.");

        var reason = (request.Reason ?? "Maintenance").Trim();
        if (reason.Length > 200) reason = reason[..200];

        var blocked = new BlockedDate
        {
            Id = Guid.NewGuid(),
            CourtId = courtId,
            ClientId = clientId,
            Date = normalizedDate,
            StartTime = startTime,
            EndTime = endTime,
            Reason = reason,
            CreatedAt = DateTime.UtcNow
        };

        _db.BlockedDates.Add(blocked);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to add blocked date for client {ClientId}", clientId);
            throw new InvalidOperationException(
                "Could not save the blocked date. Please check the values and try again.");
        }

        return new BlockedDateDto(
            blocked.Id.ToString(),
            blocked.Date.ToString("yyyy-MM-dd"),
            blocked.StartTime?.ToString("HH:mm"),
            blocked.EndTime?.ToString("HH:mm"),
            blocked.Reason);
    }

    public async Task DeleteBlockedDateAsync(Guid id, Guid clientId)
    {
        var blocked = await _db.BlockedDates
            .FirstOrDefaultAsync(b => b.Id == id && b.ClientId == clientId)
            ?? throw new KeyNotFoundException("Blocked date not found");

        _db.BlockedDates.Remove(blocked);
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════
    // PRICE RULES
    // ═══════════════════════════════════════════════════════

    public async Task<List<PriceRuleDto>> GetPriceRulesAsync(Guid clientId)
    {
        return await _db.PriceRules
            .Where(r => r.ClientId == clientId && r.IsActive)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.DayOfWeek)
            .Select(r => new PriceRuleDto(
                r.Id.ToString(),
                r.Name,
                r.DayOfWeek,
                r.StartTime.ToString("HH:mm"),
                r.EndTime.ToString("HH:mm"),
                r.PricePerHour,
                r.IsActive,
                r.Priority))
            .ToListAsync();
    }

    public async Task<PriceRuleDto> CreatePriceRuleAsync(CreatePriceRuleRequest request, Guid clientId)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80)
            throw new ArgumentException("Invalid rule name.");
        ValidatePrice(request.PricePerHour, "price per hour");
        if (request.Priority < 0 || request.Priority > 1000)
            throw new ArgumentException("Priority must be between 0 and 1000.");

        if (!TimeOnly.TryParse(request.StartTime, out var start) ||
            !TimeOnly.TryParse(request.EndTime, out var end))
            throw new ArgumentException("Invalid time format.");
        if (end != TimeOnly.MinValue && end <= start)
            throw new ArgumentException("End time must be after start time.");

        var rule = new PriceRule
        {
            Name = request.Name.Trim(),
            DayOfWeek = request.DayOfWeek,
            StartTime = start,
            EndTime = end,
            PricePerHour = request.PricePerHour,
            Priority = request.Priority,
            ClientId = clientId
        };

        _db.PriceRules.Add(rule);
        await _db.SaveChangesAsync();
        return MapPriceRuleToDto(rule);
    }

    public async Task<PriceRuleDto> UpdatePriceRuleAsync(
        Guid id, UpdatePriceRuleRequest request, Guid clientId)
    {
        var rule = await _db.PriceRules
            .FirstOrDefaultAsync(r => r.Id == id && r.ClientId == clientId)
            ?? throw new KeyNotFoundException("Price rule not found");

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80)
                throw new ArgumentException("Invalid rule name.");
            rule.Name = request.Name.Trim();
        }

        if (request.DayOfWeek is not null) rule.DayOfWeek = request.DayOfWeek;

        if (request.StartTime is not null)
        {
            if (!TimeOnly.TryParse(request.StartTime, out var st))
                throw new ArgumentException("Invalid start time.");
            rule.StartTime = st;
        }

        if (request.EndTime is not null)
        {
            if (!TimeOnly.TryParse(request.EndTime, out var en))
                throw new ArgumentException("Invalid end time.");
            rule.EndTime = en;
        }

        if (rule.EndTime != TimeOnly.MinValue && rule.EndTime <= rule.StartTime)
            throw new ArgumentException("End time must be after start time.");

        if (request.PricePerHour.HasValue)
        {
            ValidatePrice(request.PricePerHour.Value, "price per hour");
            rule.PricePerHour = request.PricePerHour.Value;
        }

        if (request.IsActive.HasValue) rule.IsActive = request.IsActive.Value;

        if (request.Priority.HasValue)
        {
            if (request.Priority.Value < 0 || request.Priority.Value > 1000)
                throw new ArgumentException("Priority must be between 0 and 1000.");
            rule.Priority = request.Priority.Value;
        }

        await _db.SaveChangesAsync();
        return MapPriceRuleToDto(rule);
    }

    public async Task DeletePriceRuleAsync(Guid id, Guid clientId)
    {
        var rule = await _db.PriceRules
            .FirstOrDefaultAsync(r => r.Id == id && r.ClientId == clientId)
            ?? throw new KeyNotFoundException("Price rule not found");
        _db.PriceRules.Remove(rule);
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════
    // VALIDATION HELPERS
    // ═══════════════════════════════════════════════════════

    private static void ValidatePrice(decimal price, string label)
    {
        if (price < 0 || price > MaxPrice)
            throw new InvalidOperationException($"Invalid {label}.");
    }

    private static void ValidateHttpsUrl(string url, string label)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2000)
            throw new InvalidOperationException($"{label} is invalid.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException($"{label} must be a valid https:// URL.");
    }

    private static List<string> ValidateAmenities(List<string>? amenities)
    {
        if (amenities is null) return new List<string>();
        if (amenities.Count > MaxAmenities)
            throw new InvalidOperationException($"At most {MaxAmenities} amenities are allowed.");
        foreach (var a in amenities)
        {
            if (string.IsNullOrWhiteSpace(a) || a.Length > MaxAmenityLength)
                throw new InvalidOperationException("Invalid amenity.");
            if (a.Contains(',') || a.Contains('<') || a.Contains('>'))
                throw new InvalidOperationException("Amenities cannot contain commas or markup.");
        }
        return amenities.Select(a => a.Trim()).ToList();
    }

    // ═══════════════════════════════════════════════════════
    // MAPPERS
    // ═══════════════════════════════════════════════════════

    private static PriceRuleDto MapPriceRuleToDto(PriceRule r) => new(
        r.Id.ToString(),
        r.Name,
        r.DayOfWeek,
        r.StartTime.ToString("HH:mm"),
        r.EndTime.ToString("HH:mm"),
        r.PricePerHour,
        r.IsActive,
        r.Priority);

    private static CourtDto MapToDto(Court c) => new(
        c.Id.ToString(),
        c.Name,
        c.Type,
        c.Indoor,
        c.PricePerHour,
        c.PeakPricePerHour,
        c.Description,
        c.Amenities,
        c.Rating,
        c.ImageUrl,
        c.Images,
        c.Status,
        c.OpenTime.ToString("HH:mm"),
        c.CloseTime.ToString("HH:mm"),
        c.Dimensions,
        c.Surface);
}