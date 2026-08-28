using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface IBookingService
{
    // ✅ UPDATED - Create booking with court selection and client
    Task<BookingDto> CreateBookingAsync(CreateBookingRequest request, Guid clientId);

    // ✅ UPDATED - Track booking with client filter
    Task<BookingDto?> TrackBookingAsync(string referenceCode, string email, Guid clientId);

    // ✅ UPDATED - Get all bookings with client filter
    Task<List<BookingDto>> GetAllBookingsAsync(Guid clientId);

    // ✅ UPDATED - Admin update with client filter
    Task<BookingDto> AdminUpdateBookingAsync(Guid id, AdminUpdateBookingRequest request, Guid clientId);

    // ✅ UPDATED - Upload payment with client filter
    Task<BookingDto> UploadPaymentScreenshotAsync(Guid id, string screenshotUrl, Guid clientId);

    // ✅ UPDATED - Auto complete with client filter
    Task AutoCompletePastBookingsAsync(Guid clientId);

    // ✅ UPDATED - Cancel expired with client filter
    Task CancelExpiredPaymentsAsync(Guid clientId);
}