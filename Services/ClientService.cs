using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.DTOs;
using PickleballBookingSystem.Interfaces;

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
}