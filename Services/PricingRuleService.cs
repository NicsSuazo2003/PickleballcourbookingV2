// Services/PricingRuleService.cs
using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Entities;
using PickleballBookingSystem.Interfaces;

namespace PickleballBookingSystem.Services;

public class PricingRuleService : IPricingRuleService
{
    private readonly AppDbContext _db;

    public PricingRuleService(AppDbContext db) => _db = db;

    public async Task<List<PricingRuleDto>> GetRulesAsync(Guid courtId, Guid clientId)
    {
        var court = await _db.Courts
            .FirstOrDefaultAsync(c => c.Id == courtId && c.ClientId == clientId);
        if (court is null)
            throw new UnauthorizedAccessException("Court not found for this client");

        var rules = await _db.PricingRules
            .Where(r => r.CourtId == courtId)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.StartTime)
            .ToListAsync();

        return rules.Select(ToDto).ToList();
    }

    public async Task<PricingRuleDto> CreateRuleAsync(
        Guid courtId, Guid clientId, CreatePricingRuleRequest request)
    {
        var court = await _db.Courts
            .FirstOrDefaultAsync(c => c.Id == courtId && c.ClientId == clientId);
        if (court is null)
            throw new UnauthorizedAccessException("Court not found for this client");

        if (!TryParseTime(request.StartTime, out var start))
            throw new ArgumentException("Invalid StartTime");
        if (!TryParseTime(request.EndTime, out var end))
            throw new ArgumentException("Invalid EndTime");
        if (end <= start)
            throw new ArgumentException("EndTime must be after StartTime");

        var rule = new PricingRule
        {
            Id = Guid.NewGuid(),
            CourtId = courtId,
            Label = request.Label.Trim(),
            Days = NormalizeDays(request.Days),
            StartTime = start,
            EndTime = end,
            PricePerHour = request.PricePerHour,
            Priority = request.Priority,
        };

        _db.PricingRules.Add(rule);
        await _db.SaveChangesAsync();
        return ToDto(rule);
    }

    public async Task<PricingRuleDto> UpdateRuleAsync(
        Guid ruleId, Guid clientId, UpdatePricingRuleRequest request)
    {
        var rule = await _db.PricingRules
            .Include(r => r.Court)
            .FirstOrDefaultAsync(r => r.Id == ruleId);
        if (rule is null || rule.Court?.ClientId != clientId)
            throw new UnauthorizedAccessException("Rule not found for this client");

        if (!TryParseTime(request.StartTime, out var start))
            throw new ArgumentException("Invalid StartTime");
        if (!TryParseTime(request.EndTime, out var end))
            throw new ArgumentException("Invalid EndTime");
        if (end <= start)
            throw new ArgumentException("EndTime must be after StartTime");

        rule.Label = request.Label.Trim();
        rule.Days = NormalizeDays(request.Days);
        rule.StartTime = start;
        rule.EndTime = end;
        rule.PricePerHour = request.PricePerHour;
        rule.Priority = request.Priority;
        rule.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return ToDto(rule);
    }

    public async Task DeleteRuleAsync(Guid ruleId, Guid clientId)
    {
        var rule = await _db.PricingRules
            .Include(r => r.Court)
            .FirstOrDefaultAsync(r => r.Id == ruleId);
        if (rule is null || rule.Court?.ClientId != clientId)
            throw new UnauthorizedAccessException("Rule not found for this client");

        _db.PricingRules.Remove(rule);
        await _db.SaveChangesAsync();
    }

    public async Task<decimal> ResolvePriceAsync(Guid courtId, DateOnly date, TimeOnly startTime)
    {
        var court = await _db.Courts
            .Include(c => c.PricingRules)
            .FirstOrDefaultAsync(c => c.Id == courtId);

        if (court is null) return 0m;
        return ResolvePriceFromCourt(court, date, startTime);
    }

    /// <summary>
    /// Pure, synchronous resolver. Callers who already have the court (with
    /// .PricingRules loaded) should call this to avoid extra DB roundtrips.
    /// </summary>
    public decimal ResolvePriceFromCourt(Court court, DateOnly date, TimeOnly startTime)
    {
        if (court.PricingRules is { Count: > 0 })
        {
            var dayKey = DayKey(date.DayOfWeek);

            var match = court.PricingRules
                .Where(r =>
                    (string.IsNullOrWhiteSpace(r.Days) ||
                     r.Days
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim().ToLowerInvariant())
                        .Contains(dayKey)) &&
                    startTime >= r.StartTime &&
                    startTime < r.EndTime)
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.StartTime)
                .FirstOrDefault();

            if (match is not null) return match.PricePerHour;
        }

        // ── Fallback (unchanged legacy behavior) ────────────────────────
        var hour = startTime.Hour;
        var isPeak = hour >= 17 && hour < 22;
        return isPeak ? court.PeakPricePerHour : court.PricePerHour;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static PricingRuleDto ToDto(PricingRule r) => new()
    {
        Id = r.Id,
        CourtId = r.CourtId,
        Label = r.Label,
        Days = r.Days,
        StartTime = r.StartTime.ToString("HH:mm"),
        EndTime = r.EndTime.ToString("HH:mm"),
        PricePerHour = r.PricePerHour,
        Priority = r.Priority,
    };

    private static bool TryParseTime(string s, out TimeOnly t)
    {
        t = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        return TimeOnly.TryParse(s, out t);
    }

    private static string NormalizeDays(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var valid = new HashSet<string> { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
        var parts = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim().ToLowerInvariant())
            .Where(valid.Contains)
            .Distinct()
            .ToList();
        return string.Join(",", parts);
    }

    private static string DayKey(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => "mon",
        DayOfWeek.Tuesday => "tue",
        DayOfWeek.Wednesday => "wed",
        DayOfWeek.Thursday => "thu",
        DayOfWeek.Friday => "fri",
        DayOfWeek.Saturday => "sat",
        DayOfWeek.Sunday => "sun",
        _ => string.Empty,
    };
}