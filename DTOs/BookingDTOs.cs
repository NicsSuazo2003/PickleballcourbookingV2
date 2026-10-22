using System.ComponentModel.DataAnnotations;

namespace PickleballBookingSystem.DTOs;

public record CreateBookingRequest(
    [Required, MinLength(2), MaxLength(80)] string CustomerName,
    [Required, EmailAddress, MaxLength(120)] string CustomerEmail,
    [Required, MaxLength(20)] string? CustomerPhone,
    [Required, RegularExpression(@"^\d{4}-\d{2}-\d{2}$")] string Date,
    [Required, MinLength(1), MaxLength(12)] List<CourtSlotGroup> CourtGroups,
    [Range(0, 100000)] decimal TotalAmount,
    [MaxLength(500)] string? Notes,
    string? Status = null,
    bool AdminOverride = false
);

public record CourtSlotGroup(
    [Required] string CourtId,
    [Required, MinLength(1), MaxLength(12)] List<SlotRequest> Slots
);

public record SlotRequest(
    [Required, RegularExpression(@"^\d{2}:\d{2}$")] string StartTime,
    [Required, RegularExpression(@"^\d{2}:\d{2}$")] string EndTime
);

public record BookingDto(
    string Id, string CourtId, string CourtName,
    string CustomerName, string CustomerEmail, string? CustomerPhone,
    string ReferenceCode, string Date, List<TimeSlotDto> Slots,
    decimal TotalAmount, string Status, string PaymentMethod, string CreatedAt,
    string? Notes, string? PaymentScreenshot, DateTime? PaymentExpiresAt,
    string? PaymentReference = null
);

public record TimeSlotDto(
    string Id, string CourtId, string Date,
    string StartTime, string EndTime, bool IsAvailable, decimal Price
);

public record AdminUpdateBookingRequest(
    [Required, RegularExpression("^(pending_payment|payment_submitted|confirmed|cancelled|rejected|refunded|completed|expired)$")]
    string Status
);

public record TrackBookingRequest(string ReferenceCode, string Email);

public record UploadPaymentScreenshotRequest(IFormFile Screenshot);

public record UploadPaymentRequest(string PaymentReference, string? PaymentMethod);

public record StaffCreateBookingRequest(
    [Required] string CourtId,
    [Required, RegularExpression(@"^\d{4}-\d{2}-\d{2}$")] string Date,
    [Required, MinLength(1), MaxLength(12)] List<StaffSlotRequest> Slots,
    [Required, MaxLength(80)] string CustomerName,
    [MaxLength(120)] string? CustomerEmail,
    [MaxLength(20)] string? CustomerPhone,
    [MaxLength(500)] string? Notes,
    [Required, RegularExpression("^(cash|gcash|pay_later|free|comp)$")] string PaymentMode,
    decimal? TotalAmount,
    [MaxLength(500)] string? StaffNotes,
    bool SendConfirmation
);

public record StaffSlotRequest(
    [Required, RegularExpression(@"^\d{2}:\d{2}$")] string StartTime,
    [Required, RegularExpression(@"^\d{2}:\d{2}$")] string EndTime
);

public record BookingSummaryDto(
    string Id, string ReferenceCode, string CourtName, string Date,
    string StartTime, string EndTime, string Status, decimal TotalAmount, string CreatedAt
);

public record RescheduleBookingRequest(
    string? CourtId,
    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$")] string? Date,
    [Required, MinLength(1), MaxLength(12)] List<StaffSlotRequest> Slots,
    [MaxLength(500)] string? Reason,
    [MaxLength(500)] string? StaffNotes
);

public record RescheduleBookingResponse(
    BookingDto Booking, List<TimeSlotDto> PreviousSlots,
    string PreviousDate, string PreviousCourtId, string PreviousCourtName,
    decimal PreviousTotalAmount, decimal NewTotalAmount,
    decimal BalanceDue, decimal RefundDue
);