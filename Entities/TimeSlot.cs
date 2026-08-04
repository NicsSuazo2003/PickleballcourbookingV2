namespace PickleballBookingSystem.Entities;

public class TimeSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }

    // ✅ NEW - Direct link to Court (optional, can use Booking.CourtId)
    public Guid? CourtId { get; set; }
    public Court? Court { get; set; }

    public DateTime Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public Booking Booking { get; set; } = null!;
}