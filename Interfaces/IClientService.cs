using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface IClientService
{
    // 🔒 PUBLIC — returns only customer-safe fields; strips internal payment config
    Task<ClientPublicDto> GetClientPublicBySubdomainAsync(string subdomain);

    // 🔒 ADMIN — returns full settings including PaymentMethods config
    Task<ClientAdminDto> GetClientAdminBySubdomainAsync(string subdomain);

    // Used everywhere internally to resolve tenant
    Task<Guid> GetClientIdBySubdomainAsync(string subdomain);

    // Admin-only write
    Task<ClientAdminDto> UpdateClientSettingsAsync(Guid clientId, UpdateClientSettingsRequest request);
}