namespace PickleballBookingSystem.Entities;

public class BlockedDate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // null → blocks ALL courts on that date (tenant-wide maintenance)
    public Guid? CourtId { get; set; }
    public Court? Court { get; set; }

    private DateTime _date;
    public DateTime Date
    {
        get => _date;
        set => _date = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    // null → blocks the entire day (StartTime == null is the day-block sentinel)
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Reason { get; set; }

    private DateTime _createdAt;
    public DateTime CreatedAt
    {
        get => _createdAt;
        set => _createdAt = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
}