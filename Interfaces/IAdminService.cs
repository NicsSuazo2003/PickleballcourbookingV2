using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface IAdminService
{
    // ✅ UPDATED - Get analytics with client filter
    Task<AnalyticsDto> GetAnalyticsAsync(Guid clientId);

    // ✅ UPDATED - Get court analytics with client filter
    Task<List<CourtAnalyticsDto>> GetCourtAnalyticsAsync(Guid clientId);
}   