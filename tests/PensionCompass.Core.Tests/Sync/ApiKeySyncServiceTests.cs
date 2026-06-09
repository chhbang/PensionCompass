using System.Text;
using PensionCompass.Core.Crypto;
using PensionCompass.Core.Sync;

namespace PensionCompass.Core.Tests.Sync;

public class ApiKeySyncServiceTests
{
    private const string Pass = "test-passphrase-1234";

    [Fact]
    public void UploadDownload_RoundTrip_PreservesAllThreeKeys()
    {
        var provider = new InMemorySyncProvider();
        var sut = new ApiKeySyncService();
        var original = new ApiKeySyncService.ApiKeyBundle(
            Claude: "sk-ant-abc",
            Gemini: "AIza-xyz",
            Gpt: "sk-openai-123");

        sut.Upload(provider, original, Pass);
        var result = sut.Download(provider, Pass);

        Assert.Equal(ApiKeySyncService.DownloadStatus.Ok, result.Status);
        Assert.True(result.WasEncrypted);
        Assert.NotNull(result.Bundle);
        Assert.Equal(original.Claude, result.Bundle!.Claude);
        Assert.Equal(original.Gemini, result.Bundle.Gemini);
        Assert.Equal(original.Gpt, result.Bundle.Gpt);
    }

    [Fact]
    public void Upload_WritesEncryptedEnvelope_NotPlaintext()
    {
        var provider = new InMemorySyncProvider();
        var sut = new ApiKeySyncService();
        var bundle = new ApiKeySyncService.ApiKeyBundle("sk-ant-SECRET", "", "");

        sut.Upload(provider, bundle, Pass);

        var raw = provider.Read("apikeys.json")!;
        Assert.True(PassphraseCipher.IsEncryptedEnvelope(raw));
        // The plaintext key must NOT appear anywhere in the stored bytes.
        Assert.DoesNotContain("sk-ant-SECRET", Encoding.UTF8.GetString(raw));
    }

    [Fact]
    public void Upload_WithoutPassphrase_Throws()
    {
        var provider = new InMemorySyncProvider();
        var sut = new ApiKeySyncService();
        var bundle = new ApiKeySyncService.ApiKeyBundle("a", "b", "c");

        Assert.Throws<ArgumentException>(() => sut.Upload(provider, bundle, ""));
    }

    [Fact]
    public void Download_FileMissing_ReturnsNotPresent()
    {
        var provider = new InMemorySyncProvider(); // nothing written yet
        var sut = new ApiKeySyncService();

        var result = sut.Download(provider, Pass);

        Assert.Equal(ApiKeySyncService.DownloadStatus.NotPresent, result.Status);
        Assert.Null(result.Bundle);
    }

    [Fact]
    public void Download_EmptyContent_ReturnsNotPresent()
    {
        var provider = new InMemorySyncProvider();
        provider.Write("apikeys.json", Array.Empty<byte>());
        var sut = new ApiKeySyncService();

        Assert.Equal(ApiKeySyncService.DownloadStatus.NotPresent, sut.Download(provider, Pass).Status);
    }

    [Fact]
    public void Download_EncryptedButNoPassphrase_ReportsNeedsPassphrase()
    {
        var provider = new InMemorySyncProvider();
        var sut = new ApiKeySyncService();
        sut.Upload(provider, new ApiKeySyncService.ApiKeyBundle("a", "b", "c"), Pass);

        var result = sut.Download(provider, passphrase: null);

        Assert.Equal(ApiKeySyncService.DownloadStatus.EncryptedNeedsPassphrase, result.Status);
        Assert.True(result.WasEncrypted);
        Assert.Null(result.Bundle);
    }

    [Fact]
    public void Download_EncryptedWithWrongPassphrase_ReportsWrongPassphrase()
    {
        var provider = new InMemorySyncProvider();
        var sut = new ApiKeySyncService();
        sut.Upload(provider, new ApiKeySyncService.ApiKeyBundle("a", "b", "c"), Pass);

        var result = sut.Download(provider, "the-wrong-passphrase");

        Assert.Equal(ApiKeySyncService.DownloadStatus.WrongPassphrase, result.Status);
        Assert.Null(result.Bundle);
    }

    [Fact]
    public void Download_CorruptJson_ReportsCorruptRatherThanThrowing()
    {
        var provider = new InMemorySyncProvider();
        provider.Write("apikeys.json", Encoding.UTF8.GetBytes("not valid json {{{"));
        var sut = new ApiKeySyncService();

        // Not an envelope, not parseable as a plaintext bundle → Corrupt, must not throw.
        Assert.Equal(ApiKeySyncService.DownloadStatus.Corrupt, sut.Download(provider, Pass).Status);
    }

    [Fact]
    public void Download_LegacyPlaintextBundle_StillReadable()
    {
        // v1.2.0 wrote plaintext apikeys.json. Upgrading users must not lose their keys.
        var provider = new InMemorySyncProvider();
        provider.Write("apikeys.json",
            Encoding.UTF8.GetBytes("""{"claude":"sk-ant-legacy","gemini":"AIza-legacy","gpt":"sk-legacy"}"""));
        var sut = new ApiKeySyncService();

        var result = sut.Download(provider, passphrase: null); // no passphrase needed for legacy plaintext

        Assert.Equal(ApiKeySyncService.DownloadStatus.Ok, result.Status);
        Assert.False(result.WasEncrypted);
        Assert.Equal("sk-ant-legacy", result.Bundle!.Claude);
        Assert.Equal("AIza-legacy", result.Bundle.Gemini);
        Assert.Equal("sk-legacy", result.Bundle.Gpt);
    }

    [Fact]
    public void Download_LegacyPlaintext_MissingFields_DefaultToEmpty()
    {
        var provider = new InMemorySyncProvider();
        provider.Write("apikeys.json", Encoding.UTF8.GetBytes("""{"claude":"sk-ant-only"}"""));
        var sut = new ApiKeySyncService();

        var result = sut.Download(provider, passphrase: null);

        Assert.Equal(ApiKeySyncService.DownloadStatus.Ok, result.Status);
        Assert.Equal("sk-ant-only", result.Bundle!.Claude);
        Assert.Equal(string.Empty, result.Bundle.Gemini);
        Assert.Equal(string.Empty, result.Bundle.Gpt);
    }

    [Fact]
    public void Upload_AllEmptyBundle_StillWritesAndRoundTrips()
    {
        // Disconnect/clear flows depend on pushing an empty bundle so other PCs see "all cleared".
        var provider = new InMemorySyncProvider();
        var sut = new ApiKeySyncService();
        var empty = new ApiKeySyncService.ApiKeyBundle("", "", "");

        sut.Upload(provider, empty, Pass);
        var result = sut.Download(provider, Pass);

        Assert.Equal(ApiKeySyncService.DownloadStatus.Ok, result.Status);
        Assert.True(result.Bundle!.IsAllEmpty);
    }

    [Fact]
    public void IsAllEmpty_FlagsCorrectly()
    {
        Assert.True(new ApiKeySyncService.ApiKeyBundle("", "", "").IsAllEmpty);
        Assert.False(new ApiKeySyncService.ApiKeyBundle("a", "", "").IsAllEmpty);
        Assert.False(new ApiKeySyncService.ApiKeyBundle("", "b", "").IsAllEmpty);
        Assert.False(new ApiKeySyncService.ApiKeyBundle("", "", "c").IsAllEmpty);
    }

    /// <summary>Trivial in-memory <see cref="ISyncProvider"/> for service-level tests.</summary>
    private sealed class InMemorySyncProvider : ISyncProvider
    {
        private readonly Dictionary<string, (byte[] Content, DateTime Mtime)> _store = new();

        public bool IsConfigured => true;

        public DateTime? GetModifiedTime(string fileName)
            => _store.TryGetValue(fileName, out var entry) ? entry.Mtime : null;

        public byte[]? Read(string fileName)
            => _store.TryGetValue(fileName, out var entry) ? entry.Content : null;

        public void Write(string fileName, byte[] content)
            => _store[fileName] = (content, DateTime.UtcNow);

        public void Delete(string fileName) => _store.Remove(fileName);

        public IReadOnlyList<string> List(string subfolder)
        {
            var prefix = subfolder + "/";
            var names = new List<string>();
            foreach (var k in _store.Keys)
                if (k.StartsWith(prefix, StringComparison.Ordinal))
                    names.Add(k.Substring(prefix.Length));
            return names;
        }
    }
}
