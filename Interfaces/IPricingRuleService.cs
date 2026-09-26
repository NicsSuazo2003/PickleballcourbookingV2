// Interfaces/IPricingRuleService.cs
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Entities;

namespace PickleballBookingSystem.Interfaces;

public interface IPricingRuleService
{
    Task<List<PricingRuleDto>> GetRulesAsync(Guid courtId, Guid clientId);
    Task<PricingRuleDto> CreateRuleAsync(Guid courtId, Guid clientId, CreatePricingRuleRequest request);
    Task<PricingRuleDto> UpdateRuleAsync(Guid ruleId, Guid clientId, UpdatePricingRuleRequest request);
    Task DeleteRuleAsync(Guid ruleId, Guid clientId);

    /// <summary>
    /// Resolves the hourly price for a slot.
    /// Consults PricingRules (highest priority wins) and falls back to
    /// the court's PricePerHour / PeakPricePerHour when no rule matches.
    /// This is what the booking flow should call.
    /// </summary>
    Task<decimal> ResolvePriceAsync(Guid courtId, DateOnly date, TimeOnly startTime);

    /// <summary>
    /// In-memory resolver. Callers who already have a Court with .PricingRules
    /// loaded should call this to avoid N DB roundtrips inside a loop.
    /// </summary>
    decimal ResolvePriceFromCourt(Court court, DateOnly date, TimeOnly startTime);
}