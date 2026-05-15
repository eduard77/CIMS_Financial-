using System.Security.Cryptography;
using System.Text;

namespace Financials.Infrastructure.Inbox;

/// <summary>
/// HMAC-SHA256 signature verification for the Pattern B inbox webhook
/// (ADR-0007). Extracted from <see cref="InboxEventDispatcher"/> so it can
/// be unit-tested in isolation against bit-flip / FixedTimeEquals
/// correctness (m-2 finding).
/// </summary>
internal static class HmacSignatureVerifier
{
    /// <summary>
    /// Returns <c>true</c> iff <paramref name="signatureHeader"/> base64-decodes
    /// to the same bytes as HMAC-SHA256(<paramref name="rawBody"/>) under
    /// <paramref name="secretBytes"/>. Comparison is constant-time
    /// (<see cref="CryptographicOperations.FixedTimeEquals"/>) so a caller
    /// that times signature verification cannot learn anything about the
    /// secret from the timing channel.
    /// </summary>
    public static bool Verify(string rawBody, string? signatureHeader, byte[] secretBytes)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        ArgumentNullException.ThrowIfNull(secretBytes);

        if (string.IsNullOrEmpty(signatureHeader))
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromBase64String(signatureHeader);
        }
        catch (FormatException)
        {
            return false;
        }

        var bodyBytes = Encoding.UTF8.GetBytes(rawBody);
        using var hmac = new HMACSHA256(secretBytes);
        var computed = hmac.ComputeHash(bodyBytes);

        return CryptographicOperations.FixedTimeEquals(provided, computed);
    }
}
