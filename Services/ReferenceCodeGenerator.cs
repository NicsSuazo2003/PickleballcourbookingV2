using System.Security.Cryptography;

namespace PickleballBookingSystem.Services;

/// <summary>
/// Cryptographically secure reference-code generator for bookings.
/// Produces codes like "BK-K7NPQ4RT2X" — 10 chars from a 30-symbol alphabet,
/// roughly 5.9 × 10^14 combinations per prefix per day.
/// </summary>
public static class ReferenceCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Generate(string prefix)
    {
        Span<byte> buf = stackalloc byte[10];
        RandomNumberGenerator.Fill(buf);

        var chars = new char[buf.Length];
        for (int i = 0; i < buf.Length; i++)
            chars[i] = Alphabet[buf[i] % Alphabet.Length];

        return $"{prefix}-{new string(chars)}";
    }
}