// Services/ClientService.cs
using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;
using System.Text.Json;

namespace PickleballBookingSystem.Services;

public class ClientService : IClientService
{
    private readonly AppDbContext _db;

    public ClientService(AppDbContext db) => _db = db;

    public async Task<ClientDto> GetClientBySubdomainAsync(string subdomain)
    {
        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.Subdomain == subdomain && c.Status == "active")
            ?? throw new KeyNotFoundException($"Client with subdomain '{subdomain}' not found");

        return new ClientDto(
            client.Id.ToString(),
            client.Name,
            client.Subdomain,
            client.LogoUrl,
            client.PrimaryColor,
            client.AccentColor,
            client.GcashNumber,
            client.GcashAccountName,
            !string.IsNullOrEmpty(client.PaymentMethods)
                ? JsonSerializer.Deserialize<object>(client.PaymentMethods)
                : null,
            client.AvailableAmenities
        );
    }

    public async Task<Guid> GetClientIdBySubdomainAsync(string subdomain)
    {
        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.Subdomain == subdomain && c.Status == "active")
            ?? throw new KeyNotFoundException($"Client with subdomain '{subdomain}' not found");

        return client.Id;
    }

    public async Task<ClientDto> UpdateClientSettingsAsync(Guid clientId, UpdateClientSettingsRequest request)
    {
        var client = await _db.Clients.FindAsync(clientId)
            ?? throw new KeyNotFoundException("Client not found");

        if (request.Name is not null) client.Name = request.Name;
        if (request.GcashNumber is not null) client.GcashNumber = request.GcashNumber;
        if (request.GcashAccountName is not null) client.GcashAccountName = request.GcashAccountName;

        if (request.PaymentMethods is not null)
        {
            client.PaymentMethods = JsonSerializer.Serialize(request.PaymentMethods);
        }

        // ⭐ NEW — cascade: strip removed amenities from every court of this client
        if (request.AvailableAmenities is not null)
        {
            var oldList = client.AvailableAmenities;
            var newList = request.AvailableAmenities;

            var removed = oldList
                .Except(newList, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (removed.Count > 0)
            {
                var courts = await _db.Courts
                    .Where(c => c.ClientId == clientId)
                    .ToListAsync();

                foreach (var court in courts)
                {
                    if (string.IsNullOrEmpty(court.AmenitiesRaw)) continue;

                    var current = court.AmenitiesRaw
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .ToList();

                    var filtered = current
                        .Where(a => !removed.Contains(a, StringComparer.OrdinalIgnoreCase))
                        .ToList();

                    if (filtered.Count != current.Count)
                    {
                        // Direct write — doesn't rely on [NotMapped] setter being tracked
                        court.AmenitiesRaw = string.Join(',', filtered);
                    }
                }
            }

            client.AvailableAmenitiesRaw = string.Join(',', newList);
        }

        await _db.SaveChangesAsync();

        return new ClientDto(
            client.Id.ToString(),
            client.Name,
            client.Subdomain,
            client.LogoUrl,
            client.PrimaryColor,
            client.AccentColor,
            client.GcashNumber,
            client.GcashAccountName,
            !string.IsNullOrEmpty(client.PaymentMethods)
                ? JsonSerializer.Deserialize<object>(client.PaymentMethods)
                : null,
            client.AvailableAmenities
        );
    }
}