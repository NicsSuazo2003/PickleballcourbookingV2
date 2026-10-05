using System.ComponentModel.DataAnnotations.Schema;

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

    // ⭐ NEW — comma-separated, mirrors Court.AmenitiesRaw
    public string AvailableAmenitiesRaw { get; set; } = string.Empty;

    // ⭐ NEW — same accessor pattern as Court.Amenities
    [NotMapped]
    public List<string> AvailableAmenities
    {
        get => AvailableAmenitiesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        set => AvailableAmenitiesRaw = string.Join(',', value);
    }

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