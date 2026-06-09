using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PensionCompass.Core.Crypto;

/// <summary>
/// Authenticated, passphrase-based encryption for the small JSON blobs we mirror to the cloud
/// (currently <c>apikeys.json</c> in drive.appdata). The threat we defend against: a user's Google
/// account is compromised, or someone gains read access to the hidden appdata folder — the API keys
/// there must NOT be readable without a secret the attacker doesn't have.
///
/// Construction (all primitives are built into .NET — no third-party crypto, keeps Core lean):
/// <list type="bullet">
/// <item><b>Key derivation:</b> PBKDF2-HMAC-SHA256, <see cref="Iterations"/> iterations, random
/// per-message 16-byte salt. Iteration count follows OWASP's 2023 PBKDF2-SHA256 guidance.</item>
/// <item><b>Encryption:</b> AES-256-GCM (authenticated) with a random per-message 12-byte nonce and
/// a 16-byte tag. GCM gives us confidentiality AND integrity — a wrong passphrase or any tampering
/// fails tag verification and throws rather than returning garbage.</item>
/// </list>
///
/// The envelope is itself JSON so it sits naturally where a plaintext JSON file used to be, and so the
/// reader can cheaply tell an encrypted file from a legacy plaintext one by probing for the
/// <c>enc</c> discriminator (see <see cref="IsEncryptedEnvelope"/>).
///
/// IMPORTANT: the passphrase is never stored alongside the ciphertext. It lives only in each device's
/// local credential vault. Losing it means the cloud copy is unrecoverable by design — the caller must
/// surface that clearly and offer "re-enter keys + set a new passphrase" as the recovery path.
/// </summary>
public static class PassphraseCipher
{
    /// <summary>Discriminator + algorithm tag written into every envelope. Bump the suffix if the
    /// scheme ever changes so old readers fail loudly instead of misinterpreting bytes.</summary>
    public const string Scheme = "PBKDF2-SHA256-AESGCM";

    private const int Version = 1;
    private const int SaltSize = 16;   // 128-bit salt
    private const int NonceSize = 12;  // 96-bit nonce — the size GCM is optimised for
    private const int TagSize = 16;    // 128-bit auth tag (GCM max)
    private const int KeySize = 32;    // AES-256
    private const int Iterations = 600_000; // OWASP 2023 PBKDF2-HMAC-SHA256

    // Accepted band for the iteration count read from an (untrusted) envelope on decrypt. The KDF runs
    // BEFORE the GCM tag is verified, so `iter` is the one parameter an attacker who can write the cloud
    // file could abuse without forging a tag — an unbounded value would force a multi-second CPU burn on
    // every device that auto-downloads the bundle at launch. We only ever WRITE Iterations, so clamping
    // to a sane band rejects tampered values (too high = DoS, too low = weakened KDF) without affecting
    // any legitimate file.
    private const int MinIterations = 100_000;
    private const int MaxIterations = 5_000_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> under <paramref name="passphrase"/> and returns the
    /// self-describing JSON envelope as UTF-8 bytes. A fresh random salt and nonce are generated per
    /// call, so encrypting the same input twice yields different ciphertext (and never reuses a
    /// (key, nonce) pair — the cardinal GCM rule).
    /// </summary>
    /// <exception cref="ArgumentNullException">plaintext is null.</exception>
    /// <exception cref="ArgumentException">passphrase is null/empty — encryption with no secret is
    /// meaningless and is rejected so callers can't accidentally produce a "decryptable by anyone" blob.</exception>
    public static byte[] Encrypt(byte[] plaintext, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("Passphrase must be non-empty.", nameof(passphrase));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(passphrase, salt);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        var envelope = new Envelope
        {
            Enc = Scheme,
            V = Version,
            Iter = Iterations,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Ct = Convert.ToBase64String(ciphertext),
            Tag = Convert.ToBase64String(tag),
        };
        return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
    }

    /// <summary>
    /// Decrypts an envelope produced by <see cref="Encrypt"/>. Throws on any problem so the caller can
    /// distinguish failure modes:
    /// <list type="bullet">
    /// <item><see cref="InvalidCipherEnvelopeException"/> — not a well-formed envelope (malformed JSON,
    /// missing/garbage fields, unknown scheme). Treat as "corrupt", not "wrong passphrase".</item>
    /// <item><see cref="CryptographicException"/> — tag verification failed: wrong passphrase OR the
    /// ciphertext was tampered with. Indistinguishable by design (that's the security property).</item>
    /// </list>
    /// </summary>
    public static byte[] Decrypt(byte[] envelopeBytes, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(envelopeBytes);
        if (string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("Passphrase must be non-empty.", nameof(passphrase));

        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(envelopeBytes, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidCipherEnvelopeException("Envelope is not valid JSON.", ex);
        }

        if (envelope is null || !string.Equals(envelope.Enc, Scheme, StringComparison.Ordinal))
            throw new InvalidCipherEnvelopeException($"Unrecognized cipher envelope (enc='{envelope?.Enc}').");
        // Reject an out-of-band iteration count BEFORE running PBKDF2 — caps a tamper-driven CPU-exhaustion
        // DoS and blocks a KDF-strength downgrade. Surfaces as "corrupt", not "wrong passphrase".
        if (envelope.Iter < MinIterations || envelope.Iter > MaxIterations)
            throw new InvalidCipherEnvelopeException(
                $"Envelope iteration count {envelope.Iter} is outside the accepted range [{MinIterations}, {MaxIterations}].");

        byte[] salt, nonce, ciphertext, tag;
        try
        {
            salt = Convert.FromBase64String(envelope.Salt ?? "");
            nonce = Convert.FromBase64String(envelope.Nonce ?? "");
            ciphertext = Convert.FromBase64String(envelope.Ct ?? "");
            tag = Convert.FromBase64String(envelope.Tag ?? "");
        }
        catch (FormatException ex)
        {
            throw new InvalidCipherEnvelopeException("Envelope field is not valid base64.", ex);
        }

        if (nonce.Length != NonceSize || tag.Length != TagSize || salt.Length == 0)
            throw new InvalidCipherEnvelopeException("Envelope salt/nonce/tag has an unexpected length.");

        var key = DeriveKey(passphrase, salt, envelope.Iter);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            // Throws CryptographicException if the tag doesn't verify (wrong passphrase / tampering).
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        return plaintext;
    }

    /// <summary>
    /// Cheap probe: does this byte payload look like one of our encrypted envelopes (vs a legacy
    /// plaintext JSON blob)? Used by readers to decide whether a passphrase is needed at all. Never
    /// throws — anything that isn't a clean envelope with the right <c>enc</c> tag returns false.
    /// </summary>
    public static bool IsEncryptedEnvelope(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return false;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("enc", out var enc)
                && enc.ValueKind == JsonValueKind.String
                && string.Equals(enc.GetString(), Scheme, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations = Iterations)
        => Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(passphrase),
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeySize);

    private sealed class Envelope
    {
        [JsonPropertyName("enc")] public string? Enc { get; set; }
        [JsonPropertyName("v")] public int V { get; set; }
        [JsonPropertyName("iter")] public int Iter { get; set; }
        [JsonPropertyName("salt")] public string? Salt { get; set; }
        [JsonPropertyName("nonce")] public string? Nonce { get; set; }
        [JsonPropertyName("ct")] public string? Ct { get; set; }
        [JsonPropertyName("tag")] public string? Tag { get; set; }
    }
}

/// <summary>
/// Thrown when a payload claims to be (or is expected to be) a <see cref="PassphraseCipher"/> envelope
/// but is structurally invalid — distinct from a <see cref="CryptographicException"/>, which means the
/// envelope was well-formed but the passphrase was wrong (or the bytes were tampered with).
/// </summary>
public sealed class InvalidCipherEnvelopeException : Exception
{
    public InvalidCipherEnvelopeException(string message) : base(message) { }
    public InvalidCipherEnvelopeException(string message, Exception inner) : base(message, inner) { }
}
