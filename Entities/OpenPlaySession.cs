// Entities/OpenPlaySession.cs
namespace PickleballBookingSystem.Entities;

public class OpenPlaySession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public Guid CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public DateTime Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public int MaxPlayers { get; set; } = 12;
    public int CurrentPlayers { get; set; } = 0;

    public decimal PricePerPlayer { get; set; }

    public string SkillLevel { get; set; } = "All Levels";
    public string? HostName { get; set; }
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}