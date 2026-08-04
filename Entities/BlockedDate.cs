namespace PickleballBookingSystem.Entities;

public class BlockedDate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ✅ Link to Court (optional - null means all courts)
    public Guid? CourtId { get; set; }
    public Court? Court { get; set; }

    public DateTime Date { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}