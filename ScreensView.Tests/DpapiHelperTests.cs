using ScreensView.Viewer.Helpers;

namespace ScreensView.Tests;

public class DpapiHelperTests
{
    [Fact]
    public void Encrypt_Decrypt_RoundTrip()
    {
        const string original = "my-secret-api-key-12345";

        var encrypted = DpapiHelper.Encrypt(original);
        var decrypted = DpapiHelper.Decrypt(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Encrypt_EmptyString_RoundTrip()
    {
        var encrypted = DpapiHelper.Encrypt(string.Empty);
        var decrypted = DpapiHelper.Decrypt(encrypted);

        Assert.Equal(string.Empty, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesValidBase64()
    {
        var encrypted = DpapiHelper.Encrypt("test-value");

        var bytes = Convert.FromBase64String(encrypted); // throws if invalid
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Encrypt_SamePlaintext_ProducesDifferentCipherEachTime()
    {
        // DPAPI adds random entropy, so two encryptions of the same value differ
        var enc1 = DpapiHelper.Encrypt("same-input");
        var enc2 = DpapiHelper.Encrypt("same-input");

        Assert.NotEqual(enc1, enc2);
    }

    [Fact]
    public void Decrypt_InvalidBase64_ThrowsException()
    {
        Assert.ThrowsAny<Exception>(() => DpapiHelper.Decrypt("not!!valid!!base64"));
    }

    [Fact]
    public void Decrypt_ValidBase64ButNotDpapi_ThrowsException()
    {
        var notDpapi = Convert.ToBase64String("random-garbage-bytes"u8.ToArray());

        Assert.ThrowsAny<Exception>(() => DpapiHelper.Decrypt(notDpapi));
    }
}
