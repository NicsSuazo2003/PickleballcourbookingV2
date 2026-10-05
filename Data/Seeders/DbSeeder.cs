// Data/Seeders/DbSeeder.cs
using PickleballBookingSystem.Entities;

namespace PickleballBookingSystem.Data;

public static class DbSeeder
{
    public static void Initialize(AppDbContext db)
    {
        // Seed the initial client + courts + admin user only if there's nothing yet.
        if (!db.Clients.Any())
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
                // Amenities start empty — admin adds them via Settings.
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
                        ImageUrl = "https://images.unsplash.com/photo-1534438327276-14e5300c3a48",
                        ImagesRaw = "https://images.unsplash.com/photo-1534438327276-14e5300c3a48",
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
                        ImageUrl = "https://images.unsplash.com/photo-1622163642998-1ea32b0bbc67",
                        ImagesRaw = "https://images.unsplash.com/photo-1622163642998-1ea32b0bbc67",
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
                        ImageUrl = "https://images.unsplash.com/photo-1554068865-24cecd4e34b8",
                        ImagesRaw = "https://images.unsplash.com/photo-1554068865-24cecd4e34b8",
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

            if (!db.Users.Any())
            {
                var admin = new User
                {
                    Id = Guid.NewGuid(),
                    Email = "admin@picklejoe.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                    Name = "Admin User",
                    Role = "admin",
                    Status = "active",
                    CreatedAt = DateTime.UtcNow,
                };

                db.Users.Add(admin);
                db.SaveChanges();
            }
        }
    }
}