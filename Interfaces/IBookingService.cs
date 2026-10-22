using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface IBookingService
{
    // ✅ Added optional IP / UA for security logging
    Task<BookingDto> CreateBookingAsync(
        CreateBookingRequest request,
        Guid clientId,
        string? ipAddress = null,
        string? userAgent = null);

    Task<BookingDto> GetBookingAsync(Guid id, Guid clientId);
    Task<BookingDto> GetBookingByReferenceAsync(string referenceCode, Guid clientId);
    Task<BookingDto> TrackBookingAsync(string referenceCode, string? email, Guid clientId);
    Task<List<BookingDto>> GetBookingsAsync(Guid clientId, DateTime? fromDate = null, DateTime? toDate = null);
    Task<List<BookingDto>> GetAllBookingsAsync(Guid clientId);

    // ✅ Added optional actor context for security logging
    Task<BookingDto> UpdateBookingStatusAsync(
        Guid id,
        string status,
        Guid clientId,
        Guid? actorUserId = null,
        string? actorEmail = null,
        string? actorIp = null,
        string? actorUA = null);

    Task<BookingDto> AdminUpdateBookingAsync(Guid id, AdminUpdateBookingRequest request, Guid clientId);
    Task<BookingDto> UploadPaymentAsync(Guid id, string screenshotBase64, string? paymentReference, Guid clientId);
    Task<BookingDto> UploadPaymentScreenshotAsync(Guid id, string? screenshotBase64, string? paymentReference, string? paymentMethod, Guid clientId);
    Task ConfirmPaymentAsync(Guid id, Guid clientId);
    Task CancelBookingAsync(Guid id, Guid clientId);
    Task AutoCompletePastBookingsAsync(Guid clientId);
    Task CancelExpiredPaymentsAsync(Guid clientId);
    Task<List<BookingSummaryDto>> TrackBookingSummariesByEmailAsync(string email, Guid clientId);
    Task<BookingDto> CreateStaffBookingAsync(StaffCreateBookingRequest request, Guid clientId);

    Task<RescheduleBookingResponse> RescheduleBookingAsync(
        Guid id,
        RescheduleBookingRequest request,
        Guid clientId);
}