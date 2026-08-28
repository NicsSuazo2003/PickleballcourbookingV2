using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface IClientService
{
    Task<ClientDto> GetClientBySubdomainAsync(string subdomain);
    Task<Guid> GetClientIdBySubdomainAsync(string subdomain);
}

public record ClientDto(
    string Id,
    string Name,
    string Subdomain,
    string? LogoUrl,
    string PrimaryColor,
    string AccentColor,
    string? GcashNumber,
    string? GcashAccountName
);