using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PensionCompass.Core.Crypto;

namespace PensionCompass.Core.Sync;

/// <summary>
/// Bridges the three per-provider API keys to a single <c>apikeys.json</c> file inside the sync
/// provider's backing store. Used by the v1.2.0 Google-account API-key-link feature: when the user
/// is connected to Google, key changes in Settings round-trip through this service to drive.appdata,
/// and on connect we re-download to repopulate the local PasswordVault cloud-cache slots.
///
/// v1.3.0 hardens this: the bundle is now <b>encrypted at rest</b> with a user passphrase via
/// <see cref="PassphraseCipher"/> before it leaves the machine. The passphrase lives only in each
/// device's local credential vault, never in the cloud — so a compromised Google account no longer
/// exposes the keys. <see cref="Download"/> still transparently reads a <i>legacy plaintext</i>
/// <c>apikeys.json</c> (written by v1.2.0) so existing users aren't locked out; the next
/// <see cref="Upload"/> rewrites it in encrypted form.
///
/// Format intentionally minimal: a flat dict of three string fields, JSON-then-encrypted. Whole-file
/// replacement on each save — payload is tiny, so we avoid partial merges or per-key timestamps.
/// Last-write-wins is fine because no realistic user races two PCs through the Settings UI
/// simultaneously.
///
/// Filesystem-folder mode (v1.0.x compat) does NOT use this service — the design deliberately
/// excludes that mode to avoid writing API keys into the user's general filesystem. drive.appdata is
/// gated by the OAuth client + Google account combo and is hidden from the Drive web UI, and now the
/// payload is additionally passphrase-encrypted.
/// </summary>
public sealed class ApiKeySyncService
{
    private const string FileName = "apikeys.json";

    public sealed record ApiKeyBundle(string Claude, string Gemini, string Gpt)
    {
        public bool IsAllEmpty
            => string.IsNullOrEmpty(Claude) && string.IsNullOrEmpty(Gemini) && string.IsNullOrEmpty(Gpt);
    }

    /// <summary>Outcome of a <see cref="Download"/>, so callers can react precisely (prompt for a
    /// passphrase, show "wrong passphrase", fall back to local cache, etc.) rather than collapsing
    /// every failure to "no bundle".</summary>
    public enum DownloadStatus
    {
        /// <summary>No <c>apikeys.json</c> exists yet (fresh Google account / after a deliberate wipe).</summary>
        NotPresent,
        /// <summary>Bundle was read successfully (see <see cref="DownloadResult.Bundle"/>).</summary>
        Ok,
        /// <summary>File is encrypted but no passphrase was supplied — the caller must collect one.</summary>
        EncryptedNeedsPassphrase,
        /// <summary>File is encrypted and the supplied passphrase did not decrypt it (or it was tampered).</summary>
        WrongPassphrase,
        /// <summary>File exists but is structurally unreadable (truncated upload, corruption).</summary>
        Corrupt,
    }

    public sealed record DownloadResult(DownloadStatus Status, ApiKeyBundle? Bundle, bool WasEncrypted)
    {
        public static readonly DownloadResult NotPresent = new(DownloadStatus.NotPresent, null, false);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Whether the provider currently has an <c>apikeys.json</c> at all. Used to tell a genuine
    /// "no bundle yet" from a transient read failure: the Google provider swallows read errors and
    /// returns null bytes (→ <see cref="DownloadStatus.NotPresent"/>), so callers about to overwrite
    /// the cloud copy can first confirm a file really isn't there before clobbering it.
    /// </summary>
    public bool RemoteBundleExists(ISyncProvider provider) => provider.GetModifiedTime(FileName) is not null;

    /// <summary>
    /// Reads and decrypts <c>apikeys.json</c> from the provider. Transparently handles three on-disk
    /// shapes: missing, encrypted envelope (v1.3.0+), and legacy plaintext (v1.2.0). Never throws —
    /// every failure mode is reported through <see cref="DownloadResult.Status"/> so a corrupt or
    /// passphrase-locked cloud file can't crash the app on launch.
    /// </summary>
    /// <param name="passphrase">Required to read an encrypted bundle; ignored for a legacy plaintext
    /// one. Pass null/empty when the caller hasn't collected a passphrase yet — an encrypted file then
    /// reports <see cref="DownloadStatus.EncryptedNeedsPassphrase"/>.</param>
    public DownloadResult Download(ISyncProvider provider, string? passphrase)
    {
        var bytes = provider.Read(FileName);
        if (bytes is null || bytes.Length == 0) return DownloadResult.NotPresent;

        if (PassphraseCipher.IsEncryptedEnvelope(bytes))
        {
            if (string.IsNullOrEmpty(passphrase))
                return new DownloadResult(DownloadStatus.EncryptedNeedsPassphrase, null, true);
            byte[] plaintext;
            try
            {
                plaintext = PassphraseCipher.Decrypt(bytes, passphrase);
            }
            catch (CryptographicException)
            {
                return new DownloadResult(DownloadStatus.WrongPassphrase, null, true);
            }
            catch (InvalidCipherEnvelopeException)
            {
                return new DownloadResult(DownloadStatus.Corrupt, null, true);
            }
            var encBundle = ParseBundle(plaintext);
            return encBundle is null
                ? new DownloadResult(DownloadStatus.Corrupt, null, true)
                : new DownloadResult(DownloadStatus.Ok, encBundle, true);
        }

        // Legacy plaintext path (v1.2.0). Read it so upgrading users keep their keys; the next Upload
        // re-encrypts.
        var bundle = ParseBundle(bytes);
        return bundle is null
            ? new DownloadResult(DownloadStatus.Corrupt, null, false)
            : new DownloadResult(DownloadStatus.Ok, bundle, false);
    }

    /// <summary>
    /// Serializes, <b>encrypts</b> with <paramref name="passphrase"/>, and hands the envelope to the
    /// provider's Write (fire-and-forget for the Google implementation). Calling with an all-empty
    /// bundle still writes — that's how the user clears their cloud keys (or how a Disconnect cleanup
    /// signals "empty" to other PCs sharing the Google account).
    /// </summary>
    /// <exception cref="ArgumentException">passphrase is null/empty. Uploading keys without a passphrase
    /// is forbidden — that's the v1.2.0 plaintext behavior we're deliberately retiring.</exception>
    public void Upload(ISyncProvider provider, ApiKeyBundle bundle, string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("A passphrase is required to upload API keys.", nameof(passphrase));

        var dto = new BundleDto(bundle.Claude, bundle.Gemini, bundle.Gpt);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
        var envelope = PassphraseCipher.Encrypt(plaintext, passphrase);
        provider.Write(FileName, envelope);
    }

    private static ApiKeyBundle? ParseBundle(byte[] json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<BundleDto>(json, JsonOptions);
            if (dto is null) return null;
            return new ApiKeyBundle(
                Claude: dto.Claude ?? string.Empty,
                Gemini: dto.Gemini ?? string.Empty,
                Gpt: dto.Gpt ?? string.Empty);
        }
        catch
        {
            return null;
        }
    }

    private sealed record BundleDto(
        [property: JsonPropertyName("claude")] string Claude,
        [property: JsonPropertyName("gemini")] string Gemini,
        [property: JsonPropertyName("gpt")] string Gpt);
}
