using PickleballBookingSystem.Entities;

namespace PickleballBookingSystem.Data;

public static class DbSeeder
{
    public static void Initialize(AppDbContext db)
    {
        // Seed Courts if empty
        if (!db.Courts.Any())
        {
            var courts = new List<Court>
            {
                new Court
                {
                    Id = Guid.NewGuid(),
                    Name = "Court 1",
                    Type = "indoor",
                    Indoor = true,
                    PricePerHour = 150,
                    Amenities = new List<string> { "WiFi", "Air Conditioning", "Lighting" },
                    Rating = 4.8,
                    Status = "active",
                    OpenTime = new TimeOnly(8, 0),
                    CloseTime = new TimeOnly(22, 0),
                    Dimensions = "44ft x 20ft",
                    Surface = "Cushion"
                },
                new Court
                {
                    Id = Guid.NewGuid(),
                    Name = "Court 2",
                    Type = "indoor",
                    Indoor = true,
                    PricePerHour = 150,
                    Amenities = new List<string> { "WiFi", "Air Conditioning", "Lighting" },
                    Rating = 4.7,
                    Status = "active",
                    OpenTime = new TimeOnly(8, 0),
                    CloseTime = new TimeOnly(22, 0),
                    Dimensions = "44ft x 20ft",
                    Surface = "Cushion"
                },
                new Court
                {
                    Id = Guid.NewGuid(),
                    Name = "Court 3",
                    Type = "outdoor",
                    Indoor = false,
                    PricePerHour = 130,
                    Amenities = new List<string> { "WiFi", "Lighting" },
                    Rating = 4.5,
                    Status = "active",
                    OpenTime = new TimeOnly(8, 0),
                    CloseTime = new TimeOnly(20, 0),
                    Dimensions = "44ft x 20ft",
                    Surface = "Hardcourt"
                }
            };

            db.Courts.AddRange(courts);
            db.SaveChanges();
        }

        // Seed admin user if empty
        if (!db.Users.Any())
        {
            var admin = new User
            {
                Id = Guid.NewGuid(),
                Email = "admin@courtside.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                Name = "Admin User",
                Role = "admin",
                Status = "active",
                CreatedAt = DateTime.UtcNow
            };

            db.Users.Add(admin);
            db.SaveChanges();
        }
    }
}