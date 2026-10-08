using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface ICourtService
{
    Task<List<CourtDto>> GetAllCourtsAsync(Guid clientId);
    Task<CourtDto> GetCourtByIdAsync(Guid id, Guid clientId);
    Task<CourtDto> CreateCourtAsync(CreateCourtRequest request, Guid clientId);
    Task<CourtDto> UpdateCourtAsync(Guid id, UpdateCourtRequest request, Guid clientId);
    Task DeleteCourtAsync(Guid id, Guid clientId);

    // ✅ UPDATED — accepts an optional excludeBookingId so the reschedule
    //    modal can see the booking's own current slots as selectable.
    Task<List<TimeSlotAvailabilityDto>> GetCourtAvailabilityAsync(
        Guid courtId,
        DateTime date,
        Guid clientId,
        Guid? excludeBookingId = null);

    Task<List<BlockedDateDto>> GetBlockedDatesAsync(Guid? courtId, Guid clientId);
    Task<BlockedDateDto> AddBlockedDateAsync(CreateBlockedDateRequest request, Guid? courtId, Guid clientId);
    Task DeleteBlockedDateAsync(Guid id, Guid clientId);

    Task<CourtDto> GetCourtAsync();
    Task<List<TimeSlotAvailabilityDto>> GetAvailabilityAsync(DateTime date);
    Task<CourtDto> UpdateCourtSettingsAsync(UpdateCourtRequest request);

    Task<List<PriceRuleDto>> GetPriceRulesAsync(Guid clientId);
    Task<PriceRuleDto> CreatePriceRuleAsync(CreatePriceRuleRequest request, Guid clientId);
    Task<PriceRuleDto> UpdatePriceRuleAsync(Guid id, UpdatePriceRuleRequest request, Guid clientId);
    Task DeletePriceRuleAsync(Guid id, Guid clientId);
}