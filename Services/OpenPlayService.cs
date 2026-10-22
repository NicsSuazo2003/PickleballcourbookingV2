using System.Data;
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
            .Include(s => s.SessionCourts).ThenInclude(sc => sc.Court)
            .OrderBy(s => s.Date).ThenBy(s => s.StartTime)
            .Take(200)
            .ToListAsync();
        return sessions.Select(MapToDto).ToList();
    }

    public async Task<OpenPlaySessionDto?> GetSessionByIdAsync(Guid id, Guid clientId)
    {
        var session = await _db.OpenPlaySessions
            .Include(s => s.Court)
            .Include(s => s.SessionCourts).ThenInclude(sc => sc.Court)
            .FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId && s.IsActive);
        return session == null ? null : MapToDto(session);
    }

    public async Task<BookingDto> JoinSessionAsync(Guid id, JoinOpenPlayRequest request, Guid clientId)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName) || request.CustomerName.Length > 80)
            throw new InvalidOperationException("A valid name is required.");
        if (string.IsNullOrWhiteSpace(request.CustomerEmail) || !request.CustomerEmail.Contains('@'))
            throw new InvalidOperationException("A valid email is required.");
        if (request.Notes is { Length: > 500 })
            throw new InvalidOperationException("Notes are too long.");

        var email = request.CustomerEmail.Trim().ToLowerInvariant();

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var session = await _db.OpenPlaySessions
                .FromSqlInterpolated($@"
                    SELECT * FROM open_play_sessions
                    WHERE ""Id"" = {id} AND ""ClientId"" = {clientId} AND ""IsActive"" = true
                    FOR UPDATE")
                .Include(s => s.Court)
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Open Play session not found");

            var sessionEnd = session.Date.Date.Add(session.EndTime.ToTimeSpan());
            if (sessionEnd < DateTime.UtcNow)
                throw new InvalidOperationException("This Open Play session has already ended.");
            if (session.CurrentPlayers >= session.MaxPlayers)
                throw new InvalidOperationException("This Open Play session is full.");

            // One join per email per session
            var alreadyJoined = await _db.Bookings.AnyAsync(b =>
                b.OpenPlaySessionId == id &&
                b.ClientId == clientId &&
                b.CustomerEmail.ToLower() == email &&
                b.Status != "cancelled" &&
                b.Status != "rejected" &&
                b.Status != "expired");
            if (alreadyJoined)
                throw new InvalidOperationException("This email has already joined this session.");

            // Cap total unpaid joins per email
            var pendingCount = await _db.Bookings.CountAsync(b =>
                b.ClientId == clientId &&
                b.CustomerEmail.ToLower() == email &&
                b.Status == "pending_payment" &&
                b.PaymentExpiresAt > DateTime.UtcNow);
            if (pendingCount >= 3)
                throw new InvalidOperationException(
                    "You have too many unpaid bookings. Complete one first.");

            var isFree = session.PricePerPlayer <= 0;
            var utcDate = DateTime.SpecifyKind(session.Date, DateTimeKind.Utc);

            var booking = new Booking
            {
                CourtId = session.CourtId,
                ClientId = clientId,
                OpenPlaySessionId = session.Id,
                CustomerName = request.CustomerName.Trim(),
                CustomerEmail = email,
                CustomerPhone = (request.CustomerPhone ?? "").Trim(),
                ReferenceCode = ReferenceCodeGenerator.Generate("OP"),
                Date = utcDate,
                TotalAmount = session.PricePerPlayer,
                Status = isFree ? "confirmed" : "pending_payment",
                PaymentMethod = isFree ? "free" : "gcash",
                Notes = request.Notes?.Trim(),
                CreatedAt = DateTime.UtcNow,
                PaymentExpiresAt = isFree ? null : DateTime.UtcNow.AddMinutes(15),
                Slots = new List<TimeSlot>
                {
                    new TimeSlot
                    {
                        CourtId = session.CourtId,
                        Date = utcDate,
                        StartTime = session.StartTime,
                        EndTime = session.EndTime,
                        Price = session.PricePerPlayer
                    }
                }
            };

            _db.Bookings.Add(booking);
            session.CurrentPlayers += 1;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            try
            {
                await _email.NotifyAdminNewBookingAsync(
                    booking.CustomerName,
                    booking.ReferenceCode + (isFree ? " [OPEN PLAY - FREE]" : " [OPEN PLAY]"),
                    booking.Date.ToString("yyyy-MM-dd"),
                    $"{session.StartTime:HH:mm}-{session.EndTime:HH:mm}",
                    isFree ? "FREE" : $"₱{booking.TotalAmount}");
            }
            catch { }

            return MapToBookingDto(booking, session.Court?.Name ?? "");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<List<PublicOpenPlayPlayerDto>> GetPublicPlayersAsync(Guid id, Guid clientId)
    {
        var session = await _db.OpenPlaySessions
            .FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId && s.IsActive)
            ?? throw new KeyNotFoundException("Open Play session not found");

        var sessionEnd = session.Date.Date.Add(session.EndTime.ToTimeSpan());
        if (sessionEnd < DateTime.UtcNow)
            throw new KeyNotFoundException("Open Play session has ended");

        var bookings = await _db.Bookings
            .Where(b => b.OpenPlaySessionId == id
                     && b.ClientId == clientId
                     && b.Status != "cancelled"
                     && b.Status != "rejected"
                     && b.Status != "expired"
                     && b.Status != "refunded")
            .OrderBy(b => b.CreatedAt)
            .Take(50)
            .ToListAsync();

        return bookings.Select(b => new PublicOpenPlayPlayerDto(
            b.Id.ToString(),
            ToDisplayName(b.CustomerName),
            b.Status,
            string.Empty /* CreatedAt hidden — was a metadata leak */)).ToList();
    }

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
            .Include(s => s.SessionCourts).ThenInclude(sc => sc.Court)
            .OrderByDescending(s => s.Date).ThenBy(s => s.StartTime)
            .Take(200)
            .ToListAsync();
        return sessions.Select(MapToDto).ToList();
    }

    public async Task<OpenPlaySessionDto> AdminCreateSessionAsync(CreateOpenPlaySessionRequest request, Guid clientId)
    {
        if (request.MaxPlayers < 2 || request.MaxPlayers > 20)
            throw new InvalidOperationException("Max players must be between 2 and 20.");

        if (request.CourtIds == null || request.CourtIds.Length == 0)
            throw new InvalidOperationException("Please select at least one court.");

        var courtGuids = new List<Guid>();
        foreach (var raw in request.CourtIds)
        {
            if (!Guid.TryParse(raw, out var g)) throw new InvalidOperationException("Invalid court id.");
            if (!courtGuids.Contains(g)) courtGuids.Add(g);
        }

        var courts = await _db.Courts
            .Where(c => courtGuids.Contains(c.Id) && c.ClientId == clientId).ToListAsync();
        if (courts.Count != courtGuids.Count)
            throw new KeyNotFoundException("One or more courts not found.");

        var ordered = courtGuids.Select(g => courts.First(c => c.Id == g)).ToList();
        var primary = ordered[0];

        var session = new OpenPlaySession
        {
            ClientId = clientId,
            CourtId = primary.Id,
            Court = primary,
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

        foreach (var g in courtGuids)
            session.SessionCourts.Add(new OpenPlaySessionCourt
            {
                OpenPlaySessionId = session.Id,
                CourtId = g,
                CreatedAt = DateTime.UtcNow
            });

        _db.OpenPlaySessions.Add(session);
        await _db.SaveChangesAsync();

        foreach (var sc in session.SessionCourts)
            sc.Court = courts.First(c => c.Id == sc.CourtId);

        return MapToDto(session);
    }

    public async Task<OpenPlaySessionDto> AdminUpdateSessionAsync(Guid id, UpdateOpenPlaySessionRequest request, Guid clientId)
    {
        var session = await _db.OpenPlaySessions
            .Include(s => s.Court)
            .Include(s => s.SessionCourts).ThenInclude(sc => sc.Court)
            .FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId)
            ?? throw new KeyNotFoundException("Open Play session not found");

        if (request.MaxPlayers < 2 || request.MaxPlayers > 20)
            throw new InvalidOperationException("Max players must be between 2 and 20.");
        if (request.MaxPlayers < session.CurrentPlayers)
            throw new InvalidOperationException("Max players cannot be less than joined count.");
        if (session.CurrentPlayers > 0 && request.PricePerPlayer != session.PricePerPlayer)
            throw new InvalidOperationException("Cannot change price after players have joined.");

        if (request.CourtIds == null || request.CourtIds.Length == 0)
            throw new InvalidOperationException("Please select at least one court.");

        var courtGuids = new List<Guid>();
        foreach (var raw in request.CourtIds)
        {
            if (!Guid.TryParse(raw, out var g)) throw new InvalidOperationException("Invalid court id.");
            if (!courtGuids.Contains(g)) courtGuids.Add(g);
        }

        var courts = await _db.Courts
            .Where(c => courtGuids.Contains(c.Id) && c.ClientId == clientId).ToListAsync();
        if (courts.Count != courtGuids.Count)
            throw new KeyNotFoundException("One or more courts not found.");

        var primary = courtGuids.Select(g => courts.First(c => c.Id == g)).First();

        session.CourtId = primary.Id;
        session.Court = primary;

        var existing = await _db.OpenPlaySessionCourts
            .Where(sc => sc.OpenPlaySessionId == session.Id).ToListAsync();
        _db.OpenPlaySessionCourts.RemoveRange(existing);
        foreach (var g in courtGuids)
            _db.OpenPlaySessionCourts.Add(new OpenPlaySessionCourt
            {
                OpenPlaySessionId = session.Id,
                CourtId = g,
                CreatedAt = DateTime.UtcNow
            });

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

        session.SessionCourts = await _db.OpenPlaySessionCourts
            .Where(sc => sc.OpenPlaySessionId == session.Id)
            .Include(sc => sc.Court).ToListAsync();

        return MapToDto(session);
    }

    // Soft-delete: archive instead of remove to preserve bookings
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
            .OrderBy(b => b.CreatedAt).ToListAsync();

        return bookings.Select(b => new OpenPlayPlayerDto(
            b.Id.ToString(), b.CustomerName, b.CustomerEmail, b.CustomerPhone,
            b.ReferenceCode, b.Status, b.PaymentMethod, b.TotalAmount,
            b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"))).ToList();
    }

    public async Task<OpenPlaySessionStatsDto> AdminGetSessionStatsAsync(Guid id, Guid clientId)
    {
        var session = await _db.OpenPlaySessions
            .FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId)
            ?? throw new KeyNotFoundException("Open Play session not found");

        var bookings = await _db.Bookings
            .Where(b => b.OpenPlaySessionId == id && b.ClientId == clientId).ToListAsync();

        var confirmed = bookings.Count(b => b.Status is "confirmed" or "completed");
        var pending = bookings.Count(b => b.Status is "pending_payment" or "payment_submitted");
        var revenue = bookings.Where(b => b.Status is "confirmed" or "completed").Sum(b => b.TotalAmount);
        var pendingRevenue = bookings.Where(b => b.Status is "pending_payment" or "payment_submitted").Sum(b => b.TotalAmount);

        return new OpenPlaySessionStatsDto(
            session.Id.ToString(), session.CurrentPlayers, session.MaxPlayers,
            revenue, pendingRevenue, confirmed, pending);
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

    private static OpenPlaySessionDto MapToDto(OpenPlaySession s)
    {
        var courts = (s.SessionCourts ?? new List<OpenPlaySessionCourt>())
            .Select(sc => new OpenPlayCourtDto(sc.CourtId.ToString(), sc.Court?.Name ?? "")).ToList();
        if (courts.Count == 0 && s.Court != null)
            courts.Add(new OpenPlayCourtDto(s.CourtId.ToString(), s.Court.Name));

        var primaryName = s.Court?.Name ?? courts.FirstOrDefault()?.Name ?? "";

        return new OpenPlaySessionDto(
            s.Id.ToString(), s.CourtId.ToString(), primaryName, courts,
            s.Date.ToString("yyyy-MM-dd"),
            s.StartTime.ToString("HH:mm"), s.EndTime.ToString("HH:mm"),
            s.MaxPlayers, s.CurrentPlayers, Math.Max(0, s.MaxPlayers - s.CurrentPlayers),
            s.PricePerPlayer, s.SkillLevel, s.HostName, s.Title, s.Description,
            ComputeStatus(s), s.IsActive, s.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    private static BookingDto MapToBookingDto(Booking b, string courtName) => new(
        b.Id.ToString(), b.CourtId.ToString(), courtName,
        b.CustomerName, b.CustomerEmail, b.CustomerPhone, b.ReferenceCode,
        b.Date.ToString("yyyy-MM-dd"),
        b.Slots.Select(s => new TimeSlotDto(
            s.Id.ToString(), (s.CourtId ?? b.CourtId).ToString(), s.Date.ToString("yyyy-MM-dd"),
            s.StartTime.ToString("HH:mm"), s.EndTime.ToString("HH:mm"), false, s.Price)).ToList(),
        b.TotalAmount, b.Status, b.PaymentMethod,
        b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        b.Notes, b.PaymentScreenshot, b.PaymentExpiresAt, b.PaymentReference);
}