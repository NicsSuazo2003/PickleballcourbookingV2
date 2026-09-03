using PickleballBookingSystem.DTOs;

namespace PickleballBookingSystem.Interfaces;

public interface IBookingService
{
   
    Task<BookingDto> CreateBookingAsync(CreateBookingRequest request, Guid clientId);

    Task<BookingDto?> TrackBookingAsync(string referenceCode, string email, Guid clientId);

    Task<List<BookingDto>> GetAllBookingsAsync(Guid clientId);

   
    Task<BookingDto> AdminUpdateBookingAsync(Guid id, AdminUpdateBookingRequest request, Guid clientId);

    Task<BookingDto> UploadPaymentScreenshotAsync(Guid id, string screenshotUrl, string paymentReference, Guid clientId);

   
    Task AutoCompletePastBookingsAsync(Guid clientId);

   
    Task CancelExpiredPaymentsAsync(Guid clientId);
}