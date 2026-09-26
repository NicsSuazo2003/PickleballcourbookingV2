// Entities/PricingRule.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickleballBookingSystem.Entities;

/// <summary>
/// A day-and-time-specific pricing rule for a court.
/// Falls back to Court.PricePerHour / PeakPricePerHour when no rule matches.
/// </summary>
[Table("PricingRules")]
public class PricingRule
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CourtId { get; set; }

    [ForeignKey(nameof(CourtId))]
    public Court? Court { get; set; }

    /// <summary>Human-readable name shown in the admin UI (e.g. "Saturday All Day").</summary>
    [MaxLength(80)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Comma-separated days: "mon,tue,wed,thu,fri,sat,sun". Empty = every day.</summary>
    [MaxLength(64)]
    public string Days { get; set; } = string.Empty;

    /// <summary>Inclusive start time (HH:mm).</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Exclusive end time (HH:mm).</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>Flat hourly rate applied when this rule matches.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal PricePerHour { get; set; }

    /// <summary>Higher wins on overlap. Default 0.</summary>
    public int Priority { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}