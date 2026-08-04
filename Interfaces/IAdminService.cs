using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface IAdminService
{
    Task<AnalyticsDto> GetAnalyticsAsync();
    Task<List<CourtAnalyticsDto>> GetCourtAnalyticsAsync();  // ✅ NEW
}