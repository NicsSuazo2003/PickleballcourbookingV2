using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickleballBookingSystem.Entities;

[Table("security_events")]
public class SecurityEventEntity
{
    [Key]
    [Column("Id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("ClientId")]
    public Guid ClientId { get; set; }

    [Column("OccurredAt")]
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    [Column("ActorUserId")]
    public Guid? ActorUserId { get; set; }

    [Column("ActorEmail")]
    public string? ActorEmail { get; set; }

    [Column("IpHash")]
    public string? IpHash { get; set; }

    [Column("IpAddress")]
    public System.Net.IPAddress? IpAddress { get; set; }

    [Column("UserAgent")]
    public string? UserAgent { get; set; }

    [Column("EventType")]
    public string EventType { get; set; } = string.Empty;

    [Column("Severity")]
    public string Severity { get; set; } = "info";

    [Column("Resource")]
    public string? Resource { get; set; }

    [Column("Message")]
    public string Message { get; set; } = string.Empty;

    [Column("Metadata")]
    public string? Metadata { get; set; }

    [Column("Success")]
    public bool Success { get; set; } = true;

    [Column("ErrorCode")]
    public string? ErrorCode { get; set; }
}