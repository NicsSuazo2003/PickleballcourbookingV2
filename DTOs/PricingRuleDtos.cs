// DTOs/PricingRuleDtos.cs
using System.ComponentModel.DataAnnotations;

namespace PickleballBookingSystem.DTOs;

public class PricingRuleDto
{
    public Guid Id { get; set; }
    public Guid CourtId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Days { get; set; } = string.Empty; // "mon,tue,..."
    public string StartTime { get; set; } = "00:00";
    public string EndTime { get; set; } = "00:00";
    public decimal PricePerHour { get; set; }
    public int Priority { get; set; }
}

public class CreatePricingRuleRequest
{
    [Required, MaxLength(80)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Days { get; set; } = string.Empty;

    [Required]
    public string StartTime { get; set; } = "08:00";

    [Required]
    public string EndTime { get; set; } = "22:00";

    [Range(0, 100000)]
    public decimal PricePerHour { get; set; }

    public int Priority { get; set; } = 0;
}

public class UpdatePricingRuleRequest : CreatePricingRuleRequest { }