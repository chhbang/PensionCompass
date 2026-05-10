using System.Text;
using PensionCompass.Core.Sync;

namespace PensionCompass.Core.Tests.Sync;

public class FilesystemFolderSyncProviderTests : IDisposable
{
    private readonly string _tempDir;

    public FilesystemFolderSyncProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PensionCompass_SyncTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void IsConfigured_FalseWhenFolderNullOrWhitespace()
    {
        Assert.False(new FilesystemFolderSyncProvider(() => null).IsConfigured);
        Assert.False(new FilesystemFolderSyncProvider(() => "").IsConfigured);
        Assert.False(new FilesystemFolderSyncProvider(() => "   ").IsConfigured);
    }

    [Fact]
    public void IsConfigured_TrueWhenFolderSet()
    {
        Assert.True(new FilesystemFolderSyncProvider(() => _tempDir).IsConfigured);
    }

    [Fact]
    public void Write_Read_RoundTrip()
    {
        var provider = new FilesystemFolderSyncProvider(() => _tempDir);
        var content = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");

        provider.Write("account.json", content);
        var read = provider.Read("account.json");

        Assert.NotNull(read);
        Assert.Equal(content, read);
    }

    [Fact]
    public void Read_ReturnsNullWhenFileMissing()
    {
        var provider = new FilesystemFolderSyncProvider(() => _tempDir);

        Assert.Null(provider.Read("nonexistent.json"));
    }

    [Fact]
    public void Read_ReturnsNullWhenFolderNotConfigured()
    {
        var provider = new FilesystemFolderSyncProvider(() => null);

        Assert.Null(provider.Read("any.json"));
    }

    [Fact]
    public void GetModifiedTime_ReturnsNullWhenFileMissing()
    {
        var provider = new FilesystemFolderSyncProvider(() => _tempDir);

        Assert.Null(provider.GetModifiedTime("nonexistent.json"));
    }

    [Fact]
    public void GetModifiedTime_ReturnsUtcMtime()
    {
        var provider = new FilesystemFolderSyncProvider(() => _tempDir);
        var before = DateTime.UtcNow.AddSeconds(-2);

        provider.Write("a.json", [1, 2, 3]);
        var mtime = provider.GetModifiedTime("a.json");

        Assert.NotNull(mtime);
        Assert.True(mtime.Value >= before);
        Assert.True(mtime.Value <= DateTime.UtcNow.AddSeconds(2));
    }

    [Fact]
    public void Write_CreatesSubdirectories_FromForwardSlashFileName()
    {
        // Logical name uses forward slashes; provider should create OS-native subdirs.
        var provider = new FilesystemFolderSyncProvider(() => _tempDir);
        var content = Encoding.UTF8.GetBytes("session-data");

        provider.Write("History/2026-05-10_153022_Claude.json", content);

        var expected = Path.Combine(_tempDir, "History", "2026-05-10_153022_Claude.json");
        Assert.True(File.Exists(expected));
        Assert.Equal(content, File.ReadAllBytes(expected));
    }

    [Fact]
    public void Read_AlsoFindsForwardSlashFile()
    {
        var provider = new FilesystemFolderSyncProvider(() => _tempDir);
        var content = Encoding.UTF8.GetBytes("session-data");
        provider.Write("History/x.json", content);

        var read = provider.Read("History/x.json");

        Assert.Equal(content, read);
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var provider = new FilesystemFolderSyncProvider(() => _tempDir);
        provider.Write("a.json", [1]);
        Assert.NotNull(provider.Read("a.json"));

        provider.Delete("a.json");

        Assert.Null(provider.Read("a.json"));
    }

    [Fact]
    public void Delete_NoOpWhenFileMissing()
    {
        var provider = new FilesystemFolderSyncProvider(() => _tempDir);

        // Should not throw.
        provider.Delete("nonexistent.json");
    }

    [Fact]
    public void DelegateIsResolvedLazily_ChangingFolderTakesEffect()
    {
        // Caller might flip the configured folder at runtime — provider should re-resolve on
        // every call rather than caching the initial value.
        var folder = (string?)null;
        var provider = new FilesystemFolderSyncProvider(() => folder);

        Assert.False(provider.IsConfigured);
        Assert.Null(provider.Read("a.json"));

        folder = _tempDir;
        Assert.True(provider.IsConfigured);
        provider.Write("a.json", [1, 2]);
        Assert.NotNull(provider.Read("a.json"));
    }
}
