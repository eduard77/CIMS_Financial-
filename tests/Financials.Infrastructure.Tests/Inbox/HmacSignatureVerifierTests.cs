using System.Security.Cryptography;
using System.Text;
using Financials.Infrastructure.Inbox;

namespace Financials.Infrastructure.Tests.Inbox;

/// <summary>
/// Direct unit tests for <see cref="HmacSignatureVerifier"/>. F1ImportSliceTests
/// already exercises the dispatcher's full happy/sad paths against a real DB;
/// this suite isolates the signature check so we can pin the bit-flip and
/// FixedTimeEquals correctness without paying the Testcontainers cost (m-2).
/// </summary>
public class HmacSignatureVerifierTests
{
    private const string Secret = "test-secret-32-chars-or-more-pls";

    private static byte[] SecretBytes => Encoding.UTF8.GetBytes(Secret);

    private static string Sign(string body)
    {
        using var hmac = new HMACSHA256(SecretBytes);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void Verify_accepts_a_matching_signature()
    {
        const string Body = """{"hello":"world"}""";
        var signature = Sign(Body);

        HmacSignatureVerifier.Verify(Body, signature, SecretBytes).Should().BeTrue();
    }

    [Fact]
    public void Verify_rejects_null_signature_header()
    {
        HmacSignatureVerifier.Verify("{}", null, SecretBytes).Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_empty_signature_header()
    {
        HmacSignatureVerifier.Verify("{}", string.Empty, SecretBytes).Should().BeFalse();
    }

    [Theory]
    [InlineData("<not base64>")]
    [InlineData("====")]
    [InlineData("@@@")]
    public void Verify_rejects_non_base64_signature_header(string sig)
    {
        HmacSignatureVerifier.Verify("{}", sig, SecretBytes).Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_signature_computed_with_a_different_secret()
    {
        const string Body = "payload";
        using var foreignHmac = new HMACSHA256(Encoding.UTF8.GetBytes("wrong-secret-32-chars-or-more!!!"));
        var foreignSig = Convert.ToBase64String(foreignHmac.ComputeHash(Encoding.UTF8.GetBytes(Body)));

        HmacSignatureVerifier.Verify(Body, foreignSig, SecretBytes).Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_signature_when_body_was_tampered_with()
    {
        const string Body = "original";
        var signature = Sign(Body);

        HmacSignatureVerifier.Verify("tampered", signature, SecretBytes).Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_single_bit_flip_in_signature()
    {
        const string Body = "payload";
        var signatureBytes = Convert.FromBase64String(Sign(Body));
        // Flip the last bit of the last byte; everything else equal.
        signatureBytes[^1] ^= 0x01;
        var corruptedSig = Convert.ToBase64String(signatureBytes);

        HmacSignatureVerifier.Verify(Body, corruptedSig, SecretBytes).Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_a_truncated_signature_of_the_correct_prefix()
    {
        // FixedTimeEquals requires same-length arrays; a length mismatch must be
        // rejected without leaking timing info. We don't observe timing here,
        // but we can at least pin that the false answer is correct.
        const string Body = "payload";
        var sigBytes = Convert.FromBase64String(Sign(Body));
        var truncated = Convert.ToBase64String(sigBytes[..16]);   // half the bytes

        HmacSignatureVerifier.Verify(Body, truncated, SecretBytes).Should().BeFalse();
    }

    [Fact]
    public void Verify_throws_on_null_secret_bytes()
    {
        var act = () => HmacSignatureVerifier.Verify("{}", "abc", null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("secretBytes");
    }

    [Fact]
    public void Verify_throws_on_null_body()
    {
        var act = () => HmacSignatureVerifier.Verify(null!, "abc", SecretBytes);
        act.Should().Throw<ArgumentNullException>().WithParameterName("rawBody");
    }
}
