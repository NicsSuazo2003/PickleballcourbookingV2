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

    /// <summary>
    /// Deserialized view of AvailableAmenitiesJson. Never throws — malformed
    /// JSON in the DB is treated as "no amenities" rather than a 500.
    /// </summary>
    [NotMapped]
    public List<AmenityItem> AvailableAmenities
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AvailableAmenitiesJson))
                return new List<AmenityItem>();
            try
            {
                var list = JsonSerializer.Deserialize<List<AmenityItem>>(AvailableAmenitiesJson);
                return list?.Where(a => !string.IsNullOrEmpty(a.Name)).ToList()
                       ?? new List<AmenityItem>();
            }
            catch (JsonException)
            {
                // Corrupt JSON — return empty rather than crash the public site.
                return new List<AmenityItem>();
            }
        }
        set => AvailableAmenitiesJson = value is null || value.Count == 0
            ? null
            : JsonSerializer.Serialize(value);
    }

    public int MaxAdvanceBookingDays { get; set; } = 90;

    [Column("bookings_enabled")]
    public bool BookingsEnabled { get; set; } = true;

    // ── NEW: fixed booking window ──────────────────────────────
    // Mode controls how online customer bookings are gated:
    //   "rolling" → allow up to MaxAdvanceBookingDays from today
    //   "fixed"   → allow only dates in [BookingOpenDate, BookingCloseDate]
    //   "closed"  → reject all online customer bookings
    // Staff manual bookings bypass this entirely.
    public string BookingWindowMode { get; set; } = "rolling";
    public DateTime? BookingOpenDate { get; set; }
    public DateTime? BookingCloseDate { get; set; }
    // ───────────────────────────────────────────────────────────

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