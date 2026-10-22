using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Entities;
using PickleballBookingSystem.Interfaces;

namespace PickleballBookingSystem.Services;

public class ClientService : IClientService
{
    private readonly AppDbContext _db;

    public ClientService(AppDbContext db) => _db = db;

    // ── PUBLIC (customer-facing) ─────────────────────────
    public async Task<ClientPublicDto> GetClientPublicBySubdomainAsync(string subdomain)
    {
        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.Subdomain == subdomain && c.Status == "active")
            ?? throw new KeyNotFoundException("Client not found");

        return MapToPublicDto(client);
    }

    // ── ADMIN (settings page) ────────────────────────────
    public async Task<ClientAdminDto> GetClientAdminBySubdomainAsync(string subdomain)
    {
        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.Subdomain == subdomain && c.Status == "active")
            ?? throw new KeyNotFoundException("Client not found");

        return MapToAdminDto(client);
    }

    public async Task<Guid> GetClientIdBySubdomainAsync(string subdomain)
    {
        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.Subdomain == subdomain && c.Status == "active")
            ?? throw new KeyNotFoundException("Client not found");
        return client.Id;
    }

    // ── UPDATE (admin only) ──────────────────────────────
    public async Task<ClientAdminDto> UpdateClientSettingsAsync(Guid clientId, UpdateClientSettingsRequest request)
    {
        var client = await _db.Clients.FindAsync(clientId)
            ?? throw new KeyNotFoundException("Client not found");

        if (request.Name is not null)
        {
            if (request.Name.Length > 120) throw new ArgumentException("Name is too long.");
            client.Name = request.Name.Trim();
        }
        if (request.GcashNumber is not null)
        {
            if (request.GcashNumber.Length > 20) throw new ArgumentException("GCash number is too long.");
            client.GcashNumber = request.GcashNumber.Trim();
        }
        if (request.GcashAccountName is not null)
        {
            if (request.GcashAccountName.Length > 120) throw new ArgumentException("Account name is too long.");
            client.GcashAccountName = request.GcashAccountName.Trim();
        }

        if (request.PaymentMethods is not null)
        {
            if (request.PaymentMethods.Count > 10)
                throw new ArgumentException("Too many payment methods.");

            foreach (var pm in request.PaymentMethods)
            {
                if (string.IsNullOrWhiteSpace(pm.Name) || pm.Name.Length > 60)
                    throw new ArgumentException("Invalid payment method name.");
                if (pm.Config.AccountNumber is { Length: > 60 })
                    throw new ArgumentException("Account number too long.");
                if (pm.Config.AccountName is { Length: > 120 })
                    throw new ArgumentException("Account name too long.");
                if (pm.Config.Instructions is { Length: > 2000 })
                    throw new ArgumentException("Instructions too long.");
                if (pm.Config.QrImageUrl is { Length: > 0 } url &&
                    (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https"))
                    throw new ArgumentException("QR image URL must be a valid https:// URL.");
            }

            var json = JsonSerializer.Serialize(request.PaymentMethods);
            if (json.Length > 8192) throw new ArgumentException("Payment methods payload too large.");
            client.PaymentMethods = json;
        }

        if (request.MaxAdvanceBookingDays is int maxDays)
        {
            if (maxDays < 0) throw new ArgumentException("Max advance booking days cannot be negative.");
            if (maxDays > 365) throw new ArgumentException("Max advance booking days cannot exceed 365.");
            client.MaxAdvanceBookingDays = maxDays;
        }

        if (request.BookingsEnabled is bool b) client.BookingsEnabled = b;

        if (request.AvailableAmenities is not null)
        {
            if (request.AvailableAmenities.Count > 30)
                throw new ArgumentException("Too many amenities.");

            foreach (var a in request.AvailableAmenities)
            {
                if (string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 60)
                    throw new ArgumentException("Invalid amenity name.");
                if (a.Icon is { Length: > 40 })
                    throw new ArgumentException("Invalid amenity icon.");
                if (a.Description is { Length: > 300 })
                    throw new ArgumentException("Amenity description too long.");
            }

            var oldList = client.AvailableAmenities ?? new List<AmenityItem>();
            var newList = request.AvailableAmenities.Select(a => new AmenityItem
            {
                Name = a.Name.Trim(),
                Icon = string.IsNullOrWhiteSpace(a.Icon) ? "Sparkles" : a.Icon,
                Description = a.Description?.Trim(),
            }).ToList();

            var newNames = newList.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = oldList.Select(a => a.Name).Where(n => !newNames.Contains(n)).ToList();

            if (removed.Count > 0)
            {
                var courts = await _db.Courts.Where(c => c.ClientId == clientId).ToListAsync();
                foreach (var court in courts)
                {
                    if (string.IsNullOrEmpty(court.AmenitiesRaw)) continue;
                    var current = court.AmenitiesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                    var filtered = current.Where(a => !removed.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
                    if (filtered.Count != current.Count)
                        court.AmenitiesRaw = string.Join(',', filtered);
                }
            }

            client.AvailableAmenities = newList;
        }

        await _db.SaveChangesAsync();
        return MapToAdminDto(client);
    }

    // ── MAPPERS ──────────────────────────────────────────

    private static List<PaymentMethodDto>? DeserializePaymentMethods(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<List<PaymentMethodDto>>(json); }
        catch { return null; }
    }

    private static ClientAdminDto MapToAdminDto(Client c) => new(
        c.Id.ToString(), c.Name, c.Subdomain, c.LogoUrl,
        c.PrimaryColor, c.AccentColor,
        c.GcashNumber, c.GcashAccountName,
        DeserializePaymentMethods(c.PaymentMethods),
        (c.AvailableAmenities ?? new List<AmenityItem>())
            .Where(a => !string.IsNullOrEmpty(a.Name))
            .Select(a => new AmenityItemDto(a.Name, a.Icon, a.Description)).ToList(),
        c.MaxAdvanceBookingDays, c.BookingsEnabled);

    private static ClientPublicDto MapToPublicDto(Client c) => new(
        c.Id.ToString(), c.Name, c.LogoUrl,
        c.PrimaryColor, c.AccentColor,
        c.GcashNumber, c.GcashAccountName,
        // Only enabled methods; strip config keys
        DeserializePaymentMethods(c.PaymentMethods)?
            .Where(m => m.Enabled)
            .Select(m => new PaymentMethodDto(
                m.Id, m.Name, m.Icon, m.Enabled,
                new PaymentMethodConfigDto(
                    m.Config.AccountNumber,
                    m.Config.AccountName,
                    m.Config.QrImageUrl,
                    m.Config.Instructions)))
            .ToList(),
        (c.AvailableAmenities ?? new List<AmenityItem>())
            .Where(a => !string.IsNullOrEmpty(a.Name))
            .Select(a => new AmenityItemDto(a.Name, a.Icon, a.Description)).ToList(),
        c.MaxAdvanceBookingDays, c.BookingsEnabled);
}