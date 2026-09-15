// Services/OpenPlayService.cs
using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Entities;
using PickleballBookingSystem.Interfaces;

namespace PickleballBookingSystem.Services;

public class OpenPlayService : IOpenPlayService
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;

    public OpenPlayService(AppDbContext db, EmailService email)
    {
        _db = db;
        _email = email;
    }

    public async Task<List<OpenPlaySessionDto>> GetUpcomingSessionsAsync(Guid clientId)
    {
        var todayDate = DateTime.UtcNow.Date;

        var sessions = await _db.OpenPlaySessions
            .Where(s => s.ClientId == clientId && s.IsActive && s.Date.Date >= todayDate)
            .Include(s => s.Court)
            .OrderBy(s => s.Date).ThenBy(s => s.StartTime)
            .ToListAsync();

        return sessions.Select(MapToDto).ToList();
    }

    public async Task<OpenPlaySessionDto?> GetSessionByIdAsync(Guid id, Guid clientId)
    {
        var session = await _db.OpenPlaySessions
            .Include(s => s.Court)
            .FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId && s.IsActive);

        return session == null ? null : MapToDto(session);
    }

    public async Task<BookingDto> JoinSessionAsync(Guid id, JoinOpenPlayRequest request, Guid clientId)
    {
        var session = await _db.OpenPlaySessions
            .Include(s => s.Court)
            .FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId && s.IsActive)
            ?? throw new KeyNotFoundException("Open Play session not found");

        var sessionEnd = session.Date.Date.Add(session.EndTime.ToTimeSpan());
        if (sessionEnd < DateTime.UtcNow)
            throw new InvalidOperationException("This Open Play session has already ended.");

        if (session.CurrentPlayers >= session.MaxPlayers)
            throw new InvalidOperationException("This Open Play session is full.");

        if (string.IsNullOrWhiteSpace(request.CustomerName))
            throw new InvalidOperationException("Name is required.");
        if (string.IsNullOrWhiteSpace(request.CustomerEmail))
            throw new InvalidOperationException("Email is required.");

        var referenceCode = $"OP-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        // ✅ FIX: Ensure Date is UTC
        var utcDate = DateTime.SpecifyKind(session.Date, DateTimeKind.Utc);

        var booking = new Booking
        {
            CourtId = session.CourtId,
            ClientId = clientId,
            OpenPlaySessionId = session.Id,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            CustomerPhone = request.CustomerPhone,
            ReferenceCode = referenceCode,
            Date = utcDate, // ✅ Fixed - now UTC
            TotalAmount = session.PricePerPlayer,
            Status = "pending_payment",
            PaymentMethod = "gcash",
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            PaymentExpiresAt = DateTime.UtcNow.AddMinutes(15),
            Slots = new List<TimeSlot>
            {
                new TimeSlot
                {
                    CourtId = session.CourtId,
                    Date = utcDate, // ✅ Fixed - now UTC
                    StartTime = session.StartTime,
                    EndTime = session.EndTime,
                    Price = session.PricePerPlayer,
                }
            }
        };

        _db.Bookings.Add(booking);
        session.CurrentPlayers += 1;

        await _db.SaveChangesAsync();

        try
        {
            await _email.NotifyAdminNewBookingAsync(
                booking.CustomerName,
                booking.ReferenceCode + " [OPEN PLAY]",
                booking.Date.ToString("yyyy-MM-dd"),
                $"{session.StartTime:HH:mm}-{session.EndTime:HH:mm}",
                $"₱{booking.TotalAmount}"
            );
        }
        catch { }

        return MapToBookingDto(booking, session.Court?.Name ?? "");
    }

    /// <summary>
    /// Public-safe player roster for a session. Returns first-name + last-initial only,
    /// plus status and join time. Never exposes email, phone, payment data, or reference codes.
    /// </summary>
    public async Task<List<PublicOpenPlayPlayerDto>> GetPublicPlayersAsync(Guid id, Guid clientId)
    {
        // Only return rosters for active, non-cancelled, non-past sessions.
        // This blocks enumeration of historic sessions.
        var session = await _db.OpenPlaySessions
            .FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId && s.IsActive)
            ?? throw new KeyNotFoundException("Open Play session not found");

        var now = DateTime.UtcNow;
        var sessionEnd = session.Date.Date.Add(session.EndTime.ToTimeSpan());
        if (sessionEnd < now)
            throw new KeyNotFoundException("Open Play session has ended");

        // Only include players who actually hold a spot. Skip cancelled/rejected/expired
        // so the count matches what a visitor would see in the session summary.
        var bookings = await _db.Bookings
            .Where(b => b.OpenPlaySessionId == id
                     && b.ClientId == clientId
                     && b.Status != "cancelled"
                     && b.Status != "rejected"
                     && b.Status != "expired"
                     && b.Status != "refunded")
            .OrderBy(b => b.CreatedAt)
            .Take(50) // hard cap — no session should exceed this, but prevents runaway payloads
            .ToListAsync();

        return bookings.Select(b => new PublicOpenPlayPlayerDto(
            b.Id.ToString(),
            ToDisplayName(b.CustomerName),
            b.Status,
            b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
        )).ToList();
    }

    /// <summary>
    /// "Juan Dela Cruz" → "Juan D." — privacy-safe display name.
    /// Single-word names are shown as-is.
    /// </summary>
    private static string ToDisplayName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "Player";
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0];
        return $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.";
    }

    public async Task<List<OpenPlaySessionDto>> AdminGetAllSessionsAsync(Guid clientId)
    {
        var sessions = await _db.OpenPlaySessions
            .Where(s => s.ClientId == clientId)
            .Include(s => s.Court)
            .OrderByDescending(s => s.Date).ThenBy(s => s.StartTime)
            .ToListAsync();

        return sessions.Select(MapToDto).ToList();
    }

    public async Task<OpenPlaySessionDto> AdminCreateSessionAsync(CreateOpenPlaySessionRequest request, Guid clientId)
    {
        if (request.MaxPlayers < 2 || request.MaxPlayers > 20)
            throw new InvalidOperationException("Max players must be between 2 and 20.");

        if (!Guid.TryParse(request.CourtId, out var courtGuid))
            throw new InvalidOperationException("Invalid court.");

        var court = await _db.Courts
            .FirstOrDefaultAsync(c => c.Id == courtGuid && c.ClientId == clientId)
            ?? throw new KeyNotFoundException("Court not found");

        // ✅ Already correct - using UTC
        var session = new OpenPlaySession
        {
            ClientId = clientId,
            CourtId = court.Id,
            Date = DateTime.SpecifyKind(DateTime.Parse(request.Date).Date, DateTimeKind.Utc),
            StartTime = TimeOnly.Parse(request.StartTime),
            EndTime = TimeOnly.Parse(request.EndTime),
            MaxPlayers = request.MaxPlayers,
            CurrentPlayers = 0,
            PricePerPlayer = request.PricePerPlayer,
            SkillLevel = string.IsNullOrWhiteSpace(request.SkillLevel) ? "All Levels" : request.SkillLevel,
            HostName = request.HostName,
            Title = request.Title,
            Description = request.Description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.OpenPlaySessions.Add(session);
        await _db.SaveChangesAsync();

        session.Court = court;
        return MapToDto(session);
    }

    public async Task<OpenPlaySessionDto> AdminUpdateSessionAsync(Guid id, UpdateOpenPlaySessionRequest request, Guid clientId)
    {
        var session = await _db.OpenPlaySessions
            .Include(s => s.Court)
            .FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId)
            ?? throw new KeyNotFoundException("Open Play session not found");

        if (request.MaxPlayers < 2 || request.MaxPlayers > 20)
            throw new InvalidOperationException("Max players must be between 2 and 20.");

        if (request.MaxPlayers < session.CurrentPlayers)
            throw new InvalidOperationException("Max players cannot be less than the number of players who already joined.");

        if (!Guid.TryParse(request.CourtId, out var courtGuid))
            throw new InvalidOperationException("Invalid court.");

        if (courtGuid != session.CourtId)
        {
            var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == courtGuid && c.ClientId == clientId)
                ?? throw new KeyNotFoundException("Court not found");
            session.CourtId = court.Id;
            session.Court = court;
        }

        // ✅ Already correct - using UTC
        session.Date = DateTime.SpecifyKind(DateTime.Parse(request.Date).Date, DateTimeKind.Utc);
        session.StartTime = TimeOnly.Parse(request.StartTime);
        session.EndTime = TimeOnly.Parse(request.EndTime);
        session.MaxPlayers = request.MaxPlayers;
        session.PricePerPlayer = request.PricePerPlayer;
        session.SkillLevel = string.IsNullOrWhiteSpace(request.SkillLevel) ? "All Levels" : request.SkillLevel;
        session.HostName = request.HostName;
        session.Title = request.Title;
        session.Description = request.Description;
        session.IsActive = request.IsActive;

        await _db.SaveChangesAsync();
        return MapToDto(session);
    }

    public async Task AdminDeleteSessionAsync(Guid id, Guid clientId)
    {
        var session = await _db.OpenPlaySessions
            .FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId)
            ?? throw new KeyNotFoundException("Open Play session not found");

        session.IsActive = false;
        await _db.SaveChangesAsync();
    }

    public async Task<List<OpenPlayPlayerDto>> AdminGetPlayersAsync(Guid id, Guid clientId)
    {
        var exists = await _db.OpenPlaySessions.AnyAsync(s => s.Id == id && s.ClientId == clientId);
        if (!exists) throw new KeyNotFoundException("Open Play session not found");

        var bookings = await _db.Bookings
            .Where(b => b.OpenPlaySessionId == id && b.ClientId == clientId)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();

        return bookings.Select(b => new OpenPlayPlayerDto(
            b.Id.ToString(),
            b.CustomerName,
            b.CustomerEmail,
            b.CustomerPhone,
            b.ReferenceCode,
            b.Status,
            b.PaymentMethod,
            b.TotalAmount,
            b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
        )).ToList();
    }

    public async Task<OpenPlaySessionStatsDto> AdminGetSessionStatsAsync(Guid id, Guid clientId)
    {
        var session = await _db.OpenPlaySessions
            .FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId)
            ?? throw new KeyNotFoundException("Open Play session not found");

        var bookings = await _db.Bookings
            .Where(b => b.OpenPlaySessionId == id && b.ClientId == clientId)
            .ToListAsync();

        var confirmed = bookings.Count(b => b.Status is "confirmed" or "completed");
        var pending = bookings.Count(b => b.Status is "pending_payment" or "payment_submitted");
        var revenue = bookings.Where(b => b.Status is "confirmed" or "completed").Sum(b => b.TotalAmount);
        var pendingRevenue = bookings.Where(b => b.Status is "pending_payment" or "payment_submitted").Sum(b => b.TotalAmount);

        return new OpenPlaySessionStatsDto(
            session.Id.ToString(),
            session.CurrentPlayers,
            session.MaxPlayers,
            revenue,
            pendingRevenue,
            confirmed,
            pending
        );
    }

    private static string ComputeStatus(OpenPlaySession s)
    {
        if (!s.IsActive) return "cancelled";

        var now = DateTime.UtcNow;
        var start = s.Date.Date.Add(s.StartTime.ToTimeSpan());
        var end = s.Date.Date.Add(s.EndTime.ToTimeSpan());

        if (end < now) return "past";
        if (s.CurrentPlayers >= s.MaxPlayers) return "full";
        if (now >= start && now <= end) return "active";
        return "upcoming";
    }

    private static OpenPlaySessionDto MapToDto(OpenPlaySession s) => new(
        s.Id.ToString(),
        s.CourtId.ToString(),
        s.Court?.Name ?? "",
        s.Date.ToString("yyyy-MM-dd"),
        s.StartTime.ToString("HH:mm"),
        s.EndTime.ToString("HH:mm"),
        s.MaxPlayers,
        s.CurrentPlayers,
        Math.Max(0, s.MaxPlayers - s.CurrentPlayers),
        s.PricePerPlayer,
        s.SkillLevel,
        s.HostName,
        s.Title,
        s.Description,
        ComputeStatus(s),
        s.IsActive,
        s.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
    );

    private static BookingDto MapToBookingDto(Booking b, string courtName)
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
}