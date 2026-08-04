using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface IBookingService
{
    // ✅ UPDATED - Create booking with court selection
    Task<BookingDto> CreateBookingAsync(CreateBookingRequest request);

    Task<BookingDto?> TrackBookingAsync(string referenceCode, string email);
    Task<List<BookingDto>> GetAllBookingsAsync();
    Task<BookingDto> AdminUpdateBookingAsync(Guid id, AdminUpdateBookingRequest request);
    Task<BookingDto> UploadPaymentScreenshotAsync(Guid id, string screenshotUrl);
    Task AutoCompletePastBookingsAsync();
    Task CancelExpiredPaymentsAsync();
}