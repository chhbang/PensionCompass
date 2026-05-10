namespace PensionCompass.Core.Sync;

/// <summary>
/// Sync provider that mirrors files into a user-specified local folder, typically one that the
/// user's own cloud client (OneDrive / Google Drive desktop / Dropbox) is already syncing for
/// them. Preserves the v1.0.x behavior so existing setups keep working through the abstraction.
///
/// Resolves the folder path lazily through a delegate so that changing the SyncFolder setting
/// at runtime takes effect without rebuilding this provider.
/// </summary>
public sealed class FilesystemFolderSyncProvider : ISyncProvider
{
    private readonly Func<string?> _folderProvider;

    public FilesystemFolderSyncProvider(Func<string?> folderProvider)
    {
        _folderProvider = folderProvider ?? throw new ArgumentNullException(nameof(folderProvider));
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_folderProvider());

    public DateTime? GetModifiedTime(string fileName)
    {
        var path = ResolvePath(fileName);
        if (path is null || !File.Exists(path)) return null;
        return File.GetLastWriteTimeUtc(path);
    }

    public byte[]? Read(string fileName)
    {
        var path = ResolvePath(fileName);
        if (path is null || !File.Exists(path)) return null;
        try
        {
            return File.ReadAllBytes(path);
        }
        catch
        {
            // Locked, permission denied, or transient filesystem error — treat as missing.
            return null;
        }
    }

    public void Write(string fileName, byte[] content)
    {
        var path = ResolvePath(fileName);
        if (path is null) return;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, content);
        }
        catch
        {
            // Best-effort — never fail the user-facing op because a sync mirror write failed.
        }
    }

    public void Delete(string fileName)
    {
        var path = ResolvePath(fileName);
        if (path is null || !File.Exists(path)) return;
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>
    /// Maps a logical file name (forward slashes for subdirectories) to a concrete absolute path
    /// under the configured folder. Returns null when the folder isn't configured. Forward-slash
    /// separators are normalized to the OS native separator before composition.
    /// </summary>
    private string? ResolvePath(string fileName)
    {
        var folder = _folderProvider();
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var normalized = fileName.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(folder, normalized);
    }
}
