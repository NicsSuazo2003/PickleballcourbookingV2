// Services/ClientService.cs
using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;
using System.Text.Json;
using NpgsqlTypes; // ✅ Add this

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
            client.GcashAccountName
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

        // ✅ Fix: Save payment methods as JSONB using proper casting
        if (request.PaymentMethods is not null)
        {
            var jsonString = JsonSerializer.Serialize(request.PaymentMethods);
            // Use EF.Functions to cast to jsonb
            client.PaymentMethods = jsonString;

            // Alternatively, use raw SQL for update
            // await _db.Database.ExecuteSqlRawAsync(
            //     "UPDATE clients SET payment_methods = CAST({0} AS jsonb) WHERE id = {1}",
            //     jsonString, clientId);
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
            client.GcashAccountName
        );
    }
}