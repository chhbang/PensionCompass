namespace PensionCompass.Core.Sync;

/// <summary>
/// Pluggable backing store for state mirroring (the "remote" side that <c>StateStore</c> writes
/// to alongside its always-canonical LocalState copy). The filesystem-folder implementation
/// preserves the v1.0.x sync-folder behavior; future implementations (Google Drive, etc.) will
/// fit the same contract.
///
/// All paths are virtual — implementations decide how to map a logical <c>fileName</c> like
/// <c>"account.json"</c> or <c>"History/2026-05-10_153022_Claude.json"</c> to their own
/// concrete storage. Forward-slash subdirectories are part of the logical name.
/// </summary>
public interface ISyncProvider
{
    /// <summary>
    /// True when the provider is configured and ready to accept reads/writes. False means the
    /// provider is a no-op (LocalState only); calling any of the other methods is a contract
    /// violation that providers must handle gracefully (e.g. return null / no-op).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>UTC last-modified time of the named file, or null if it doesn't exist.</summary>
    DateTime? GetModifiedTime(string fileName);

    /// <summary>Reads the named file's raw bytes, or null if it doesn't exist or isn't readable.</summary>
    byte[]? Read(string fileName);

    /// <summary>
    /// Writes raw bytes to the named file, creating any virtual subdirectories implied by the name.
    /// Implementations are best-effort — failures (offline, locked, permission-denied) should be
    /// swallowed so that the user-facing operation that triggered the write doesn't fail.
    /// </summary>
    void Write(string fileName, byte[] content);

    /// <summary>Deletes the named file. No-op when the file doesn't exist. Best-effort like <see cref="Write"/>.</summary>
    void Delete(string fileName);
}
