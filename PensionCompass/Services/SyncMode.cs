namespace PensionCompass.Services;

/// <summary>
/// Selects which <see cref="PensionCompass.Core.Sync.ISyncProvider"/> the app constructs at startup.
/// </summary>
public enum SyncMode
{
    /// <summary>LocalState only — no remote mirroring.</summary>
    None,
    /// <summary>v1.0.x-style sync: mirror to a user-specified local folder (typically backed by a
    /// cloud-sync client like OneDrive / Google Drive desktop / Dropbox).</summary>
    FilesystemFolder,
    /// <summary>Direct Google Drive integration via <c>drive.appdata</c> scope (v1.1.0+).</summary>
    GoogleDrive,
}
