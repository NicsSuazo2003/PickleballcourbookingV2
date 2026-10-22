using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Net.Http.Headers;

namespace PickleballBookingSystem.Services;

public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly IHttpClientFactory _httpFactory;

    private const string OUTER_BG = "#0D1A0D";
    private const string BODY_BG = "#1A2E1A";
    private const string CARD_BG = "#243024";
    private const string BANNER_BG = "#2A3A2A";
    private const string DIVIDER = "#324232";
    private const string ACCENT = "#D4AF37";
    private const string ACCENT_TEXT = "#0D1A0D";
    private const string ACCENT_SOFT = "#EAD79E";
    private const string CYAN = "#22D3EE";
    private const string TEXT_CREAM = "#F5F0E8";
    private const string TEXT_CREAM_MUTED = "#B8B0A0";
    private const string TEXT_CREAM_SOFT = "#E8E0D2";
    private const string DANGER = "#E74C3C";
    private const string WARNING = "#F39C12";
    private const string PURPLE = "#A78BFA";

    private const string ADMIN_URL = "https://centercourt-booking.vercel.app/admin";

    public EmailService(IConfiguration config, ILogger<EmailService> logger, IHttpClientFactory httpFactory)
    {
        _config = config;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    private static string E(string? s) =>
        System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

    private static string FormatTime(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        input = input.Trim();
        if (Regex.IsMatch(input, @"\b(AM|PM)\b", RegexOptions.IgnoreCase)) return input;
        var match = Regex.Match(input, @"^(\d{1,2}):(\d{2})");
        if (!match.Success) return input;
        if (!int.TryParse(match.Groups[1].Value, out var hour)) return input;
        var minute = match.Groups[2].Value;
        var period = hour >= 12 ? "PM" : "AM";
        var hour12 = hour % 12 == 0 ? 12 : hour % 12;
        return $"{hour12}:{minute} {period}";
    }

    private static string FormatTimeRange(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        if (input.Contains('|'))
        {
            return string.Join(" | ", input.Split('|').Select(part =>
            {
                var trimmed = part.Trim();
                var lastColon = trimmed.LastIndexOf(':');
                if (lastColon > 0)
                {
                    var courtName = trimmed.Substring(0, lastColon).Trim();
                    var timeRange = trimmed.Substring(lastColon + 1).Trim();
                    var parts = Regex.Split(timeRange, @"\s*[-–—]\s*");
                    if (parts.Length == 2)
                        return $"{courtName}: {FormatTime(parts[0])} – {FormatTime(parts[1])}";
                }
                return trimmed;
            }));
        }

        var split = Regex.Split(input.Trim(), @"\s*[-–—]\s*");
        return split.Length == 2 ? $"{FormatTime(split[0])} – {FormatTime(split[1])}" : FormatTime(input);
    }

    private static string FormatDate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return DateTime.TryParse(input, out var dt) ? dt.ToString("MMMM d, yyyy") : input;
    }

    private static string WrapLayout(string bannerTitle, string contentHtml) => $@"
<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<meta name='color-scheme' content='dark light'>
<title>Center Court</title>
</head>
<body style='margin:0;padding:0;background-color:{OUTER_BG};font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Helvetica,Arial,sans-serif;'>
  <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='background-color:{OUTER_BG};padding:24px 12px;'>
    <tr><td align='center'>
      <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='max-width:560px;background-color:{BODY_BG};border-radius:16px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.5);'>
        <tr><td style='background-color:{BANNER_BG};border-bottom:1px solid {DIVIDER};padding:26px 32px 22px;text-align:left;'>
          <div style='font-size:11px;font-weight:800;letter-spacing:2.5px;color:{ACCENT};text-transform:uppercase;margin-bottom:6px;'>CENTER COURT</div>
          <div style='font-size:24px;font-weight:800;color:{TEXT_CREAM};line-height:1.2;margin:0;letter-spacing:-0.3px;'>{E(bannerTitle)}</div>
        </td></tr>
        <tr><td style='padding:32px;'>{contentHtml}</td></tr>
        <tr><td style='padding:20px 32px 26px;border-top:1px solid {DIVIDER};'>
          <div style='font-size:11px;color:{TEXT_CREAM_MUTED};text-align:center;line-height:1.6;'>
            Book Your Court. Play Your Game.<br>Automated message — please do not reply directly.
          </div>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";

    private static string KvRow(string label, string value, bool isLast = false)
    {
        var border = isLast ? "" : $"border-bottom:1px solid {DIVIDER};";
        return $@"
              <tr>
                <td style='padding:14px 0;{border}'>
                  <div style='font-size:10px;font-weight:800;letter-spacing:1.8px;color:{TEXT_CREAM_MUTED};text-transform:uppercase;margin-bottom:5px;'>{E(label)}</div>
                  <div style='font-size:16px;font-weight:600;color:{TEXT_CREAM};line-height:1.35;'>{E(value)}</div>
                </td>
              </tr>";
    }

    private static string CtaButton(string url, string text) => $@"
              <table role='presentation' cellpadding='0' cellspacing='0' border='0' style='margin-top:28px;'>
                <tr><td align='center' style='border-radius:10px;background-color:{ACCENT};'>
                  <a href='{E(url)}' target='_blank' style='display:inline-block;padding:13px 30px;font-size:14px;font-weight:700;color:{ACCENT_TEXT};text-decoration:none;border-radius:10px;'>{E(text)}</a>
                </td></tr>
              </table>";

    private static string StatusChip(string text, string color) => $@"
              <div style='display:inline-block;padding:6px 14px;border-radius:999px;background-color:{color}22;border:1px solid {color}55;margin-bottom:24px;'>
                <span style='font-size:11px;font-weight:800;letter-spacing:1.5px;color:{color};text-transform:uppercase;'>— {E(text)}</span>
              </div>";

    // ═════════════════════════════════════════════════════
    // 1. New Booking (admin)
    // ═════════════════════════════════════════════════════
    public async Task NotifyAdminNewBookingAsync(string customerName, string referenceCode, string date, string time, string amount)
    {
        try
        {
            var content = $@"
              <p style='margin:0 0 22px;font-size:15px;line-height:1.65;color:{TEXT_CREAM_SOFT};'>
                A new booking has come in. Review the details below and confirm once payment is verified.
              </p>
              <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                {KvRow("Customer", customerName)}
                {KvRow("Reference", referenceCode)}
                {KvRow("Schedule", $"{FormatDate(date)} · {FormatTimeRange(time)}")}
                {KvRow("Amount", amount, isLast: true)}
              </table>
              {CtaButton(ADMIN_URL, "Review in Admin Panel")}";

            var html = WrapLayout("New Booking", content);
            await SendAsync(_config["Brevo:AdminEmail"],
                _config["Brevo:AdminEmail"],
                "Admin",
                $"🔔 New Booking: {referenceCode} — {customerName}",
                html);
        }
        catch (Exception ex) { _logger.LogError(ex, "Admin notification failed"); }
    }

    // 2-6. Customer notifications — same structure, values pass through E() automatically
    public async Task NotifyCustomerBookingConfirmedAsync(
        string customerEmail, string customerName, string referenceCode,
        string date, string time, string? amount = null)
    {
        try
        {
            var frontendUrl = _config["App:FrontendUrl"] ?? "https://centercourt-booking.vercel.app";
            var content = $@"
              <p style='margin:0 0 8px;font-size:15px;color:{TEXT_CREAM_SOFT};'>Hi {E(customerName)},</p>
              <p style='margin:0 0 24px;font-size:15px;line-height:1.65;color:{TEXT_CREAM_SOFT};'>Your booking has been <strong style='color:{CYAN};'>confirmed</strong>. See you on the court!</p>
              {StatusChip("Confirmed", CYAN)}
              <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                {KvRow("Reference", referenceCode)}
                {KvRow("Schedule", $"{FormatDate(date)} · {FormatTimeRange(time)}")}
                {(string.IsNullOrWhiteSpace(amount) ? "" : KvRow("Amount Paid", amount))}
                {KvRow("Status", "Confirmed", isLast: true)}
              </table>
              {CtaButton($"{frontendUrl}/track", "Track Your Booking")}";

            await SendAsync(customerEmail, customerEmail, customerName,
                $"✅ Booking Confirmed: {referenceCode}", WrapLayout("Booking Confirmed", content));
        }
        catch (Exception ex) { _logger.LogError(ex, "Customer confirm email failed"); }
    }

    public async Task NotifyCustomerBookingRejectedAsync(
        string customerEmail, string customerName, string referenceCode,
        string date, string time, string? reason = null, string? amount = null)
    {
        try
        {
            var frontendUrl = _config["App:FrontendUrl"] ?? "https://centercourt-booking.vercel.app";
            var reasonBlock = string.IsNullOrWhiteSpace(reason) ? "" :
                $"<div style='margin-top:20px;padding:14px 16px;border-left:3px solid {DANGER};background-color:{DANGER}15;border-radius:6px;'><div style='font-size:10px;font-weight:800;letter-spacing:1.8px;color:{TEXT_CREAM_MUTED};text-transform:uppercase;margin-bottom:5px;'>Reason</div><div style='font-size:14px;color:{TEXT_CREAM};line-height:1.55;'>{E(reason)}</div></div>";

            var content = $@"
              <p style='margin:0 0 8px;font-size:15px;color:{TEXT_CREAM_SOFT};'>Hi {E(customerName)},</p>
              <p style='margin:0 0 24px;font-size:15px;line-height:1.65;color:{TEXT_CREAM_SOFT};'>Unfortunately, your booking could not be approved at this time.</p>
              {StatusChip("Not Approved", DANGER)}
              <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                {KvRow("Reference", referenceCode)}
                {KvRow("Schedule", $"{FormatDate(date)} · {FormatTimeRange(time)}")}
                {(string.IsNullOrWhiteSpace(amount) ? "" : KvRow("Amount Paid", amount))}
                {KvRow("Status", "Not Approved", isLast: true)}
              </table>
              {reasonBlock}
              <p style='margin:24px 0 0;font-size:14px;line-height:1.65;color:{TEXT_CREAM_MUTED};'>If you believe this is a mistake, please get in touch.</p>
              {CtaButton($"{frontendUrl}/track", "Track Your Booking")}";

            await SendAsync(customerEmail, customerEmail, customerName,
                $"❌ Booking Not Approved: {referenceCode}", WrapLayout("Booking Not Approved", content));
        }
        catch (Exception ex) { _logger.LogError(ex, "Rejection email failed"); }
    }

    public async Task NotifyCustomerBookingCancelledAsync(
        string customerEmail, string customerName, string referenceCode,
        string date, string time, string? reason = null, string? amount = null)
    {
        try
        {
            var frontendUrl = _config["App:FrontendUrl"] ?? "https://centercourt-booking.vercel.app";
            var reasonBlock = string.IsNullOrWhiteSpace(reason) ? "" :
                $"<div style='margin-top:20px;padding:14px 16px;border-left:3px solid {WARNING};background-color:{WARNING}15;border-radius:6px;'><div style='font-size:10px;font-weight:800;letter-spacing:1.8px;color:{TEXT_CREAM_MUTED};text-transform:uppercase;margin-bottom:5px;'>Reason</div><div style='font-size:14px;color:{TEXT_CREAM};line-height:1.55;'>{E(reason)}</div></div>";

            var content = $@"
              <p style='margin:0 0 8px;font-size:15px;color:{TEXT_CREAM_SOFT};'>Hi {E(customerName)},</p>
              <p style='margin:0 0 24px;font-size:15px;line-height:1.65;color:{TEXT_CREAM_SOFT};'>Your booking has been <strong style='color:{WARNING};'>cancelled</strong>.</p>
              {StatusChip("Cancelled", WARNING)}
              <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                {KvRow("Reference", referenceCode)}
                {KvRow("Schedule", $"{FormatDate(date)} · {FormatTimeRange(time)}")}
                {(string.IsNullOrWhiteSpace(amount) ? "" : KvRow("Amount Paid", amount))}
                {KvRow("Status", "Cancelled", isLast: true)}
              </table>
              {reasonBlock}
              <p style='margin:24px 0 0;font-size:14px;line-height:1.65;color:{TEXT_CREAM_MUTED};'>Want to play again? You can start a fresh booking anytime.</p>
              {CtaButton($"{frontendUrl}/track", "Track Your Booking")}";

            await SendAsync(customerEmail, customerEmail, customerName,
                $"⚠️ Booking Cancelled: {referenceCode}", WrapLayout("Booking Cancelled", content));
        }
        catch (Exception ex) { _logger.LogError(ex, "Cancel email failed"); }
    }

    public async Task NotifyCustomerBookingRefundedAsync(
        string customerEmail, string customerName, string referenceCode,
        string date, string time, string? amount = null, string? reason = null)
    {
        try
        {
            var frontendUrl = _config["App:FrontendUrl"] ?? "https://centercourt-booking.vercel.app";
            var content = $@"
              <p style='margin:0 0 8px;font-size:15px;color:{TEXT_CREAM_SOFT};'>Hi {E(customerName)},</p>
              <p style='margin:0 0 24px;font-size:15px;line-height:1.65;color:{TEXT_CREAM_SOFT};'>Your booking has been <strong style='color:{PURPLE};'>refunded</strong>.</p>
              {StatusChip("Refunded", PURPLE)}
              <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                {KvRow("Reference", referenceCode)}
                {KvRow("Schedule", $"{FormatDate(date)} · {FormatTimeRange(time)}")}
                {(string.IsNullOrWhiteSpace(amount) ? "" : KvRow("Amount Refunded", amount))}
                {KvRow("Status", "Refunded", isLast: true)}
              </table>
              <p style='margin:24px 0 0;font-size:14px;line-height:1.65;color:{TEXT_CREAM_MUTED};'>Refunds process in 3–5 business days.</p>
              {CtaButton($"{frontendUrl}/", "Book Again")}";

            await SendAsync(customerEmail, customerEmail, customerName,
                $"💜 Booking Refunded: {referenceCode}", WrapLayout("Booking Refunded", content));
        }
        catch (Exception ex) { _logger.LogError(ex, "Refund email failed"); }
    }

    public async Task NotifyCustomerBookingRescheduledAsync(
        string customerEmail, string customerName, string referenceCode,
        string oldDate, string oldTime, string oldCourtName,
        string newDate, string newTime, string newCourtName,
        string? amount = null, string? reason = null)
    {
        try
        {
            var frontendUrl = _config["App:FrontendUrl"] ?? "https://centercourt-booking.vercel.app";
            var reasonBlock = string.IsNullOrWhiteSpace(reason) ? "" :
                $"<div style='margin-top:20px;padding:14px 16px;border-left:3px solid {ACCENT};background-color:{ACCENT}15;border-radius:6px;'><div style='font-size:10px;font-weight:800;letter-spacing:1.8px;color:{TEXT_CREAM_MUTED};text-transform:uppercase;margin-bottom:5px;'>Note from the team</div><div style='font-size:14px;color:{TEXT_CREAM};line-height:1.55;'>{E(reason)}</div></div>";

            var content = $@"
              <p style='margin:0 0 8px;font-size:15px;color:{TEXT_CREAM_SOFT};'>Hi {E(customerName)},</p>
              <p style='margin:0 0 24px;font-size:15px;line-height:1.65;color:{TEXT_CREAM_SOFT};'>Your booking has been <strong style='color:{ACCENT};'>moved</strong>.</p>
              {StatusChip("Rescheduled", ACCENT)}
              <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                {KvRow("Reference", referenceCode)}
                {KvRow("Previous Schedule", $"{FormatDate(oldDate)} · {FormatTimeRange(oldTime)}")}
                {KvRow("Previous Court", oldCourtName)}
                {KvRow("New Schedule", $"{FormatDate(newDate)} · {FormatTimeRange(newTime)}")}
                {KvRow("New Court", newCourtName)}
                {(string.IsNullOrWhiteSpace(amount) ? "" : KvRow("New Total", amount))}
              </table>
              {reasonBlock}
              {CtaButton($"{frontendUrl}/track", "Track Your Booking")}";

            await SendAsync(customerEmail, customerEmail, customerName,
                $"🔄 Booking Rescheduled: {referenceCode}", WrapLayout("Booking Rescheduled", content));
        }
        catch (Exception ex) { _logger.LogError(ex, "Reschedule email failed"); }
    }

    public async Task SendPasswordResetEmailAsync(string name, string email, string rawToken, int expiryMinutes)
    {
        var frontendUrl = (_config["App:FrontendUrl"] ?? "https://centercourt-booking.vercel.app").TrimEnd('/');
        var resetUrl = $"{frontendUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        var content = $@"
              <p style='margin:0 0 8px;font-size:15px;color:{TEXT_CREAM_SOFT};'>Hi {E(name)},</p>
              <p style='margin:0 0 24px;font-size:15px;line-height:1.65;color:{TEXT_CREAM_SOFT};'>We received a request to reset your password.</p>
              {StatusChip("Password Reset", ACCENT)}
              <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                {KvRow("Account", email)}
                {KvRow("Link expires in", $"{expiryMinutes} minutes", isLast: true)}
              </table>
              {CtaButton(resetUrl, "Reset Password")}
              <div style='margin-top:24px;padding:14px 16px;border-left:3px solid {WARNING};background-color:{WARNING}15;border-radius:6px;'>
                <div style='font-size:13px;color:{TEXT_CREAM};line-height:1.55;'><strong>Didn't request this?</strong> You can safely ignore this email.</div>
              </div>";

        try
        {
            await SendAsync(email, email, name,
                "🔐 Reset Your Password — Center Court", WrapLayout("Reset Your Password", content));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password reset email failed");
            throw;
        }
    }

    private async Task SendAsync(string? toEmail, string? toAddress, string toName, string subject, string html)
    {
        var apiKey = _config["Brevo:ApiKey"];
        var senderEmail = _config["Brevo:SenderEmail"];
        var senderName = _config["Brevo:SenderName"] ?? "Center Court";

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(senderEmail) ||
            string.IsNullOrEmpty(toEmail) || !toEmail.Contains('@'))
        {
            _logger.LogWarning("Email config incomplete — skipping {Subject}", subject);
            return;
        }

        var client = _httpFactory.CreateClient("brevo");
        var payload = new
        {
            sender = new { email = senderEmail, name = senderName },
            to = new[] { new { email = toEmail, name = ToSafeName(toName) } },
            subject,
            htmlContent = html
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Add("api-key", apiKey);

        using var resp = await client.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Brevo failed: {Status}", (int)resp.StatusCode);   // status only
        }
        else
        {
            _logger.LogInformation("Email sent to {To} ({Subject})", toEmail, subject);
        }
    }

    private static string ToSafeName(string name) =>
        Regex.Replace(name ?? "", @"[^\p{L}\s\.'-]", "").Trim() is { Length: > 0 } n ? n : "Customer";
}