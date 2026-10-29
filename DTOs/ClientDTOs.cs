namespace PickleballBookingSystem.DTOs;

// 🔒 PUBLIC — customer-facing settings endpoint
public record ClientPublicDto(
    string Id,
    string Name,
    string? LogoUrl,
    string? PrimaryColor,
    string? AccentColor,
    string? GcashNumber,
    string? GcashAccountName,
    List<PaymentMethodDto>? PaymentMethods,
    List<AmenityItemDto> AvailableAmenities,
    int MaxAdvanceBookingDays,
    bool BookingsEnabled,
    // ── NEW: fixed booking window ──
    string BookingWindowMode,
    string? BookingOpenDate,   // yyyy-MM-dd or null
    string? BookingCloseDate   // yyyy-MM-dd or null
);

// 🔒 ADMIN — settings page
public record ClientAdminDto(
    string Id,
    string Name,
    string Subdomain,
    string? LogoUrl,
    string? PrimaryColor,
    string? AccentColor,
    string? GcashNumber,
    string? GcashAccountName,
    List<PaymentMethodDto>? PaymentMethods,
    List<AmenityItemDto> AvailableAmenities,
    int MaxAdvanceBookingDays,
    bool BookingsEnabled,
    // ── NEW: fixed booking window ──
    string BookingWindowMode,
    string? BookingOpenDate,
    string? BookingCloseDate
);