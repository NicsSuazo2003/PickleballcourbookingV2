using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface ICourtService
{
    // ✅ NEW - Multi-court methods
    Task<List<CourtDto>> GetAllCourtsAsync();
    Task<CourtDto> GetCourtByIdAsync(Guid id);
    Task<CourtDto> CreateCourtAsync(CreateCourtRequest request);
    Task<CourtDto> UpdateCourtAsync(Guid id, UpdateCourtRequest request);
    Task DeleteCourtAsync(Guid id);

    // ✅ UPDATED - Get availability for specific court
    Task<List<TimeSlotAvailabilityDto>> GetCourtAvailabilityAsync(Guid courtId, DateTime date);

    // ✅ UPDATED - Blocked dates with court filter
    Task<List<BlockedDateDto>> GetBlockedDatesAsync(Guid? courtId = null);
    Task<BlockedDateDto> AddBlockedDateAsync(CreateBlockedDateRequest request, Guid? courtId = null);
    Task DeleteBlockedDateAsync(Guid id);

    // ⚠️ DEPRECATED - Single court methods (keep for backward compatibility)
    Task<CourtDto> GetCourtAsync();
    Task<List<TimeSlotAvailabilityDto>> GetAvailabilityAsync(DateTime date);
    Task<CourtDto> UpdateCourtSettingsAsync(UpdateCourtRequest request);

    // Price Rules
    Task<List<PriceRuleDto>> GetPriceRulesAsync();
    Task<PriceRuleDto> CreatePriceRuleAsync(CreatePriceRuleRequest request);
    Task<PriceRuleDto> UpdatePriceRuleAsync(Guid id, UpdatePriceRuleRequest request);
    Task DeletePriceRuleAsync(Guid id);
}