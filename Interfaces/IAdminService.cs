using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface IAdminService
{
    Task<AnalyticsDto> GetAnalyticsAsync(Guid clientId);
    Task<List<CourtAnalyticsDto>> GetCourtAnalyticsAsync(Guid clientId);

    Task<List<UserDto>> GetStaffByClientAsync(Guid clientId);
    Task<UserDto> CreateStaffAsync(CreateStaffRequest request, Guid clientId);

    // 🔒 New parameter: actingUserId — used to reject self-targeting
    Task UpdateStaffStatusAsync(Guid userId, string status, Guid clientId, Guid actingUserId);
    Task DeleteStaffAsync(Guid userId, Guid clientId, Guid actingUserId);
}