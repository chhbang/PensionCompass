namespace PensionCompass.Core.Sync;

/// <summary>
/// Used when the user has no sync target configured. Reports as not-configured and silently
/// no-ops on every operation; <see cref="GetModifiedTime"/> and <see cref="Read"/> return null
/// so callers correctly treat LocalState as the only source of truth.
/// </summary>
public sealed class NoopSyncProvider : ISyncProvider
{
    public static readonly NoopSyncProvider Instance = new();

    public bool IsConfigured => false;
    public DateTime? GetModifiedTime(string fileName) => null;
    public byte[]? Read(string fileName) => null;
    public void Write(string fileName, byte[] content) { }
    public void Delete(string fileName) { }
}
