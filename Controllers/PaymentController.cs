using Microsoft.AspNetCore.Mvc;

namespace PickleballBookingSystem.Controllers;

/// <summary>
/// Stub payment endpoint removed pending security review.
/// Real payment processing must:
///   - require [Authorize] on the caller,
///   - verify the caller owns the booking,
///   - derive the amount server-side (never from the request body),
///   - use an idempotency key (ClientRequestId header),
///   - persist the transaction result before returning.
/// </summary>
[ApiController, Route("api/payments")]
public class PaymentController : ControllerBase
{
    // No endpoints exposed.
}