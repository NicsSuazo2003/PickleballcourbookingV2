using System;
using System.Collections.Generic;

namespace PickleballBookingSystem.Entities;

public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourtId { get; set; }
    public Court Court { get; set; } = null!;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;

    // ✅ Ensure this is always UTC
    private DateTime _date;
    public DateTime Date
    {
        get => _date;
        set => _date = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "pending_payment";
    public string PaymentMethod { get; set; } = "gcash";

    private DateTime _createdAt;
    public DateTime CreatedAt
    {
        get => _createdAt;
        set => _createdAt = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private DateTime? _paymentExpiresAt;
    public DateTime? PaymentExpiresAt
    {
        get => _paymentExpiresAt;
        set => _paymentExpiresAt = value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
    }

    public string? PaymentScreenshot { get; set; }
    public string? PaymentReference { get; set; }
    public string? Notes { get; set; }
    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public Guid? OpenPlaySessionId { get; set; }
    public OpenPlaySession? OpenPlaySession { get; set; }
    public ICollection<TimeSlot> Slots { get; set; } = new List<TimeSlot>();
}