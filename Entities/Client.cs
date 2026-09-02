// Entities/Client.cs
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

    [Column(TypeName = "jsonb")] // ✅ Add this attribute
    public string? PaymentMethods { get; set; }

    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "active";

    public ICollection<Court> Courts { get; set; } = new List<Court>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}