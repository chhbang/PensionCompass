using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PensionCompass.Core.Crypto;

namespace PensionCompass.Core.Tests.Crypto;

public class PassphraseCipherTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Encrypt_Then_Decrypt_RoundTrips()
    {
        var plaintext = Bytes("""{"claude":"sk-ant-abc","gemini":"AIza-xyz","gpt":"sk-openai-1"}""");
        const string pass = "correct horse battery staple";

        var envelope = PassphraseCipher.Encrypt(plaintext, pass);
        var decrypted = PassphraseCipher.Decrypt(envelope, pass);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_SameInputTwice_ProducesDifferentCiphertext()
    {
        // Random per-call salt + nonce → no deterministic ciphertext, and crucially no (key, nonce)
        // reuse across calls. Both envelopes must still decrypt back to the same plaintext.
        var plaintext = Bytes("same secret");
        const string pass = "pw";

        var a = PassphraseCipher.Encrypt(plaintext, pass);
        var b = PassphraseCipher.Encrypt(plaintext, pass);

        Assert.NotEqual(a, b);
        Assert.Equal(plaintext, PassphraseCipher.Decrypt(a, pass));
        Assert.Equal(plaintext, PassphraseCipher.Decrypt(b, pass));
    }

    [Fact]
    public void Decrypt_WrongPassphrase_ThrowsCryptographic()
    {
        var envelope = PassphraseCipher.Encrypt(Bytes("secret"), "right");

        // Tag verification fails → CryptographicException (AuthenticationTagMismatchException, a
        // subclass), NOT a structural error and NOT garbage data. ThrowsAny accepts the subclass.
        Assert.ThrowsAny<CryptographicException>(() => PassphraseCipher.Decrypt(envelope, "wrong"));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographic()
    {
        var envelope = PassphraseCipher.Encrypt(Bytes("secret payload here"), "pw");
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(envelope)!;
        var ct = Convert.FromBase64String(doc["ct"].GetString()!);
        ct[0] ^= 0xFF; // flip a byte
        doc["ct"] = JsonSerializer.SerializeToElement(Convert.ToBase64String(ct));
        var tampered = JsonSerializer.SerializeToUtf8Bytes(doc);

        Assert.ThrowsAny<CryptographicException>(() => PassphraseCipher.Decrypt(tampered, "pw"));
    }

    [Fact]
    public void Decrypt_TamperedTag_ThrowsCryptographic()
    {
        var envelope = PassphraseCipher.Encrypt(Bytes("secret payload here"), "pw");
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(envelope)!;
        var tag = Convert.FromBase64String(doc["tag"].GetString()!);
        tag[0] ^= 0x01;
        doc["tag"] = JsonSerializer.SerializeToElement(Convert.ToBase64String(tag));
        var tampered = JsonSerializer.SerializeToUtf8Bytes(doc);

        Assert.ThrowsAny<CryptographicException>(() => PassphraseCipher.Decrypt(tampered, "pw"));
    }

    [Fact]
    public void Decrypt_OutOfRangeIterationCount_ThrowsInvalidEnvelope_WithoutRunningKdf()
    {
        // An attacker who can write the cloud file could set a huge iteration count to force a
        // CPU-exhaustion DoS (PBKDF2 runs before the tag is verified). The clamp must reject it as a
        // structural error — and do so fast, without actually running 2e9 PBKDF2 rounds.
        var envelope = PassphraseCipher.Encrypt(Bytes("secret"), "pw");
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(envelope)!;
        doc["iter"] = JsonSerializer.SerializeToElement(2_000_000_000);
        var tampered = JsonSerializer.SerializeToUtf8Bytes(doc);

        Assert.Throws<InvalidCipherEnvelopeException>(() => PassphraseCipher.Decrypt(tampered, "pw"));

        // And the lower bound blocks a KDF-strength downgrade.
        doc["iter"] = JsonSerializer.SerializeToElement(1);
        var weakened = JsonSerializer.SerializeToUtf8Bytes(doc);
        Assert.Throws<InvalidCipherEnvelopeException>(() => PassphraseCipher.Decrypt(weakened, "pw"));
    }

    [Fact]
    public void Decrypt_MalformedJson_ThrowsInvalidEnvelope()
    {
        Assert.Throws<InvalidCipherEnvelopeException>(
            () => PassphraseCipher.Decrypt(Bytes("not json {{{"), "pw"));
    }

    [Fact]
    public void Decrypt_UnknownScheme_ThrowsInvalidEnvelope()
    {
        var notOurs = Bytes("""{"enc":"ROT13","salt":"AAAA","nonce":"AAAA","ct":"","tag":"AAAA"}""");
        Assert.Throws<InvalidCipherEnvelopeException>(() => PassphraseCipher.Decrypt(notOurs, "pw"));
    }

    [Fact]
    public void Decrypt_PlaintextLegacyBundle_ThrowsInvalidEnvelope()
    {
        // A legacy plaintext apikeys.json must NOT be silently mistaken for an envelope.
        var legacy = Bytes("""{"claude":"sk-ant","gemini":"","gpt":""}""");
        Assert.Throws<InvalidCipherEnvelopeException>(() => PassphraseCipher.Decrypt(legacy, "pw"));
    }

    [Fact]
    public void Encrypt_EmptyPassphrase_Throws()
    {
        Assert.Throws<ArgumentException>(() => PassphraseCipher.Encrypt(Bytes("x"), ""));
    }

    [Fact]
    public void Decrypt_EmptyPassphrase_Throws()
    {
        var envelope = PassphraseCipher.Encrypt(Bytes("x"), "pw");
        Assert.Throws<ArgumentException>(() => PassphraseCipher.Decrypt(envelope, ""));
    }

    [Fact]
    public void Encrypt_EmptyPlaintext_RoundTrips()
    {
        var envelope = PassphraseCipher.Encrypt(Array.Empty<byte>(), "pw");
        var decrypted = PassphraseCipher.Decrypt(envelope, "pw");
        Assert.Empty(decrypted);
    }

    [Fact]
    public void Encrypt_UnicodePassphrase_RoundTrips()
    {
        // Korean passphrase — UTF-8 derivation must be stable.
        var plaintext = Bytes("비밀 데이터");
        const string pass = "내 암호구문 123 🔐";

        var envelope = PassphraseCipher.Encrypt(plaintext, pass);
        Assert.Equal(plaintext, PassphraseCipher.Decrypt(envelope, pass));
    }

    [Fact]
    public void IsEncryptedEnvelope_TrueForOurOutput_FalseOtherwise()
    {
        var envelope = PassphraseCipher.Encrypt(Bytes("x"), "pw");

        Assert.True(PassphraseCipher.IsEncryptedEnvelope(envelope));
        Assert.False(PassphraseCipher.IsEncryptedEnvelope(Bytes("""{"claude":"sk-ant"}""")));
        Assert.False(PassphraseCipher.IsEncryptedEnvelope(Bytes("garbage")));
        Assert.False(PassphraseCipher.IsEncryptedEnvelope(Array.Empty<byte>()));
        Assert.False(PassphraseCipher.IsEncryptedEnvelope(null));
    }

    [Fact]
    public void Envelope_DoesNotContainPlaintext()
    {
        // Sanity: neither the secret nor the passphrase may leak into the envelope. Use long,
        // distinctive strings (with '-', which the random base64 fields can't contain as this exact
        // run) so the check can't false-positive on an incidental short substring like "pw".
        const string secret = "SUPER-SECRET-KEY-VALUE-DO-NOT-LEAK";
        const string passphrase = "DISTINCTIVE-PASSPHRASE-NEVER-STORED-IN-ENVELOPE";
        var envelope = PassphraseCipher.Encrypt(Bytes(secret), passphrase);
        var asText = Encoding.UTF8.GetString(envelope);
        Assert.DoesNotContain(secret, asText);
        Assert.DoesNotContain(passphrase, asText);
    }
}
