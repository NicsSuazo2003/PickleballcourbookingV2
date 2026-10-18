namespace PickleballBookingSystem.DTOs;

public record CreateBookingRequest(
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    string Date,
    List<CourtSlotGroup> CourtGroups,
    decimal TotalAmount,
    string? Notes,
    string? Status = null,
    bool AdminOverride = false
);

public record CourtSlotGroup(
    string CourtId,
    List<SlotRequest> Slots
);

public record SlotRequest(string StartTime, string EndTime);

public record BookingDto(
    string Id,
    string CourtId,
    string CourtName,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    string ReferenceCode,
    string Date,
    List<TimeSlotDto> Slots,
    decimal TotalAmount,
    string Status,
    string PaymentMethod,
    string CreatedAt,
    string? Notes,
    string? PaymentScreenshot,
    DateTime? PaymentExpiresAt,
    string? PaymentReference = null
);

public record TimeSlotDto(
    string Id,
    string CourtId,   // 👈 ADDED
    string Date,
    string StartTime,
    string EndTime,
    bool IsAvailable,
    decimal Price
);

public record AdminUpdateBookingRequest(string Status);

public record TrackBookingRequest(string ReferenceCode, string Email);

public record UploadPaymentScreenshotRequest(IFormFile Screenshot);

public record UploadPaymentRequest(
    string PaymentReference,
    string? PaymentMethod
);

public record StaffCreateBookingRequest(
    string CourtId,
    string Date,
    List<StaffSlotRequest> Slots,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    string? Notes,
    string PaymentMode,
    decimal? TotalAmount,
    string? StaffNotes,
    bool SendConfirmation
);

public record StaffSlotRequest(
    string StartTime,
    string EndTime
);

public record BookingSummaryDto(
    string Id,
    string ReferenceCode,
    string CourtName,
    string Date,
    string StartTime,
    string EndTime,
    string Status,
    decimal TotalAmount,
    string CreatedAt
);

public record RescheduleBookingRequest(
    string? CourtId,
    string? Date,
    List<StaffSlotRequest> Slots,
    string? Reason,
    string? StaffNotes
);

public record RescheduleBookingResponse(
    BookingDto Booking,
    List<TimeSlotDto> PreviousSlots,
    string PreviousDate,
    string PreviousCourtId,
    string PreviousCourtName,
    decimal PreviousTotalAmount,
    decimal NewTotalAmount,
    decimal BalanceDue,
    decimal RefundDue
);