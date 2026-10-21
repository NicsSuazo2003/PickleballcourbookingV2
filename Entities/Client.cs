using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace PickleballBookingSystem.Entities;

public class Client
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string PrimaryColor { get; set; } = "#1A2E1A";
    public string AccentColor { get; set; } = "#C9A94E";
    public string? GcashNumber { get; set; }
    public string? GcashAccountName { get; set; }

    [Column(TypeName = "jsonb")]
    public string? PaymentMethods { get; set; }

    [Column(TypeName = "jsonb")]
    public string? AvailableAmenitiesJson { get; set; }

    [NotMapped]
    public List<AmenityItem> AvailableAmenities
    {
        get => string.IsNullOrEmpty(AvailableAmenitiesJson)
            ? new List<AmenityItem>()
            : JsonSerializer.Deserialize<List<AmenityItem>>(AvailableAmenitiesJson)
              ?? new List<AmenityItem>();
        set => AvailableAmenitiesJson = JsonSerializer.Serialize(value);
    }

    public int MaxAdvanceBookingDays { get; set; } = 90;

    // 👇 THE FIX — explicitly map to the snake_case DB column
    [Column("bookings_enabled")]
    public bool BookingsEnabled { get; set; } = true;

    private DateTime _createdAt;
    public DateTime CreatedAt
    {
        get => _createdAt;
        set => _createdAt = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public string Status { get; set; } = "active";

    public ICollection<Court> Courts { get; set; } = new List<Court>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}

public class AmenityItem
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "Sparkles";
    public string? Description { get; set; }
}