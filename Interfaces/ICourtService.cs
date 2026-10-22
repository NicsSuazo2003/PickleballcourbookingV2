using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface ICourtService
{
    // ── Courts ──
    Task<List<CourtDto>> GetAllCourtsAsync(Guid clientId);
    Task<CourtDto> GetCourtByIdAsync(Guid id, Guid clientId);
    Task<CourtDto> CreateCourtAsync(CreateCourtRequest request, Guid clientId);
    Task<CourtDto> UpdateCourtAsync(Guid id, UpdateCourtRequest request, Guid clientId);
    Task DeleteCourtAsync(Guid id, Guid clientId);

    // ── Availability ──
    Task<List<TimeSlotAvailabilityDto>> GetCourtAvailabilityAsync(
        Guid courtId,
        DateTime date,
        Guid clientId,
        Guid? excludeBookingId = null);

    // ── Blocked dates ──
    Task<List<BlockedDateDto>> GetBlockedDatesAsync(Guid? courtId, Guid clientId);
    Task<BlockedDateDto> AddBlockedDateAsync(CreateBlockedDateRequest request, Guid? courtId, Guid clientId);
    Task DeleteBlockedDateAsync(Guid id, Guid clientId);

    // ── Price rules ──
    Task<List<PriceRuleDto>> GetPriceRulesAsync(Guid clientId);
    Task<PriceRuleDto> CreatePriceRuleAsync(CreatePriceRuleRequest request, Guid clientId);
    Task<PriceRuleDto> UpdatePriceRuleAsync(Guid id, UpdatePriceRuleRequest request, Guid clientId);
    Task DeletePriceRuleAsync(Guid id, Guid clientId);

    // ⚠️ Deprecated single-tenant methods have been REMOVED:
    //   GetCourtAsync(), GetAvailabilityAsync(), UpdateCourtSettingsAsync()
    // They had no client filter and would leak cross-tenant data.
}