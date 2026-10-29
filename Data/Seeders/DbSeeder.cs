// Data/Seeders/DbSeeder.cs
using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Entities;

namespace PickleballBookingSystem.Data;

public static class DbSeeder
{
    public static void Initialize(AppDbContext db, IConfiguration config, ILogger logger)
    {
        if (db.Clients.Any())
        {
            return;
        }

        using var tx = db.Database.BeginTransaction();
        try
        {
            var client = new Client
            {
                Id = Guid.NewGuid(),
                Name = "PickleJoe",
                Subdomain = "picklejoe",
                LogoUrl = null,
                PrimaryColor = "#1A2E1A",
                AccentColor = "#C9A94E",
                GcashNumber = "0917 234 5678",
                GcashAccountName = "PickleJoe Courts",
                CreatedAt = DateTime.UtcNow,
                Status = "active",
                MaxAdvanceBookingDays = 90,
                BookingsEnabled = true,
                // ── NEW: default to rolling so existing behavior is unchanged ──
                BookingWindowMode = "rolling",
                BookingOpenDate = null,
                BookingCloseDate = null,
                // ─────────────────────────────────────────────────────────────────
                AvailableAmenitiesJson = null,
            };

            db.Clients.Add(client);
            db.SaveChanges();

            var clientId = client.Id;

            if (!db.Courts.Any())
            {
                var courts = new List<Court>
                {
                    new Court
                    {
                        Id = Guid.NewGuid(),
                        ClientId = clientId,
                        Name = "Cedar Court",
                        Type = "indoor",
                        Indoor = true,
                        PricePerHour = 350,
                        PeakPricePerHour = 450,
                        Description = "Premium indoor court with professional-grade flooring",
                        ImageUrl = UnsplashUrl("photo-1534438327276-14e5300c3a48"),
                        ImagesRaw = UnsplashUrl("photo-1534438327276-14e5300c3a48"),
                        AmenitiesRaw = "WiFi,Air Conditioning,Lighting,Showers",
                        Rating = 4.8,
                        Status = "active",
                        OpenTime = new TimeOnly(8, 0),
                        CloseTime = new TimeOnly(22, 0),
                        Dimensions = "44ft x 20ft",
                        Surface = "Cushion",
                    },
                    new Court
                    {
                        Id = Guid.NewGuid(),
                        ClientId = clientId,
                        Name = "Pine Grove Court",
                        Type = "outdoor",
                        Indoor = false,
                        PricePerHour = 280,
                        PeakPricePerHour = 380,
                        Description = "Open-air court surrounded by greenery",
                        ImageUrl = UnsplashUrl("photo-1622163642998-1ea32b0bbc67"),
                        ImagesRaw = UnsplashUrl("photo-1622163642998-1ea32b0bbc67"),
                        AmenitiesRaw = "WiFi,Lighting,Parking",
                        Rating = 4.5,
                        Status = "active",
                        OpenTime = new TimeOnly(8, 0),
                        CloseTime = new TimeOnly(20, 0),
                        Dimensions = "44ft x 20ft",
                        Surface = "Hardcourt",
                    },
                    new Court
                    {
                        Id = Guid.NewGuid(),
                        ClientId = clientId,
                        Name = "Mosswood Arena",
                        Type = "indoor",
                        Indoor = true,
                        PricePerHour = 500,
                        PeakPricePerHour = 650,
                        Description = "Flagship court with stadium seating",
                        ImageUrl = UnsplashUrl("photo-1554068865-24cecd4e34b8"),
                        ImagesRaw = UnsplashUrl("photo-1554068865-24cecd4e34b8"),
                        AmenitiesRaw = "WiFi,Air Conditioning,Lighting,Showers,Pro Shop",
                        Rating = 4.9,
                        Status = "active",
                        OpenTime = new TimeOnly(8, 0),
                        CloseTime = new TimeOnly(22, 0),
                        Dimensions = "44ft x 20ft",
                        Surface = "Premium Cushion",
                    },
                };

                db.Courts.AddRange(courts);
                db.SaveChanges();
            }

            var adminEmail = config["Seed:AdminEmail"]?.Trim().ToLowerInvariant();
            var adminPassword = config["Seed:AdminPassword"];
            var adminName = config["Seed:AdminName"] ?? "Admin";

            if (!string.IsNullOrWhiteSpace(adminEmail))
            {
                if (string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 12)
                {
                    throw new InvalidOperationException(
                        "Seed:AdminPassword must be set and at least 12 characters long when " +
                        "Seed:AdminEmail is provided.");
                }

                if (!db.Users.Any(u => u.Email.ToLower() == adminEmail))
                {
                    var admin = new User
                    {
                        Id = Guid.NewGuid(),
                        Email = adminEmail,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                        Name = adminName,
                        Role = "admin",
                        ClientId = clientId,
                        Status = "active",
                        CreatedAt = DateTime.UtcNow,
                    };

                    db.Users.Add(admin);
                    db.SaveChanges();
                    logger.LogWarning(
                        "Seeded admin {Email}. CHANGE THIS PASSWORD IMMEDIATELY after first login " +
                        "and remove Seed:AdminEmail/Seed:AdminPassword from the environment.",
                        adminEmail);
                }
            }
            else
            {
                logger.LogInformation(
                    "No admin seeded (Seed:AdminEmail not configured). " +
                    "Create the first admin manually via bcrypt hash or environment config.");
            }

            tx.Commit();
            logger.LogInformation("Database seeding completed for client {Subdomain}", client.Subdomain);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            logger.LogError(ex, "Database seeding FAILED — all changes rolled back");
            throw;
        }
    }

    private static string UnsplashUrl(string id) =>
        $"https://images.unsplash.com/{id}?auto=format&fit=crop&w=1600&q=80";
}