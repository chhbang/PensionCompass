using System.Text;
using PensionCompass.Core.Sync;

namespace PensionCompass.Core.Tests.Sync;

public class ApiKeySyncServiceTests
{
    [Fact]
    public void UploadDownload_RoundTrip_PreservesAllThreeKeys()
    {
        var provider = new InMemorySyncProvider();
        var sut = new ApiKeySyncService();
        var original = new ApiKeySyncService.ApiKeyBundle(
            Claude: "sk-ant-abc",
            Gemini: "AIza-xyz",
            Gpt: "sk-openai-123");

        sut.Upload(provider, original);
        var roundTripped = sut.Download(provider);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Claude, roundTripped!.Claude);
        Assert.Equal(original.Gemini, roundTripped.Gemini);
        Assert.Equal(original.Gpt, roundTripped.Gpt);
    }

    [Fact]
    public void Download_FileMissing_ReturnsNull()
    {
        var provider = new InMemorySyncProvider(); // nothing written yet
        var sut = new ApiKeySyncService();

        Assert.Null(sut.Download(provider));
    }

    [Fact]
    public void Download_EmptyContent_ReturnsNull()
    {
        var provider = new InMemorySyncProvider();
        provider.Write("apikeys.json", Array.Empty<byte>());
        var sut = new ApiKeySyncService();

        Assert.Null(sut.Download(provider));
    }

    [Fact]
    public void Download_CorruptJson_ReturnsNullRatherThanThrowing()
    {
        var provider = new InMemorySyncProvider();
        provider.Write("apikeys.json", Encoding.UTF8.GetBytes("not valid json {{{"));
        var sut = new ApiKeySyncService();

        // Corrupt payload mustn't crash the app on launch — surface as "no remote bundle".
        Assert.Null(sut.Download(provider));
    }

    [Fact]
    public void Download_MissingFieldsInJson_DefaultsToEmptyStrings()
    {
        // A partial bundle (e.g. user only configured Claude on PC1) should still load cleanly:
        // missing fields become empty rather than rejecting the whole payload.
        var provider = new InMemorySyncProvider();
        provider.Write("apikeys.json", Encoding.UTF8.GetBytes("""{"claude":"sk-ant-only"}"""));
        var sut = new ApiKeySyncService();

        var bundle = sut.Download(provider);

        Assert.NotNull(bundle);
        Assert.Equal("sk-ant-only", bundle!.Claude);
        Assert.Equal(string.Empty, bundle.Gemini);
        Assert.Equal(string.Empty, bundle.Gpt);
    }

    [Fact]
    public void Upload_AllEmptyBundle_StillWritesFile()
    {
        // Disconnect/clear flows depend on being able to push an empty bundle so other PCs
        // sharing the Google account see "all keys cleared" rather than the old values.
        var provider = new InMemorySyncProvider();
        var sut = new ApiKeySyncService();
        var empty = new ApiKeySyncService.ApiKeyBundle("", "", "");

        sut.Upload(provider, empty);
        var roundTripped = sut.Download(provider);

        Assert.NotNull(roundTripped);
        Assert.Equal(string.Empty, roundTripped!.Claude);
        Assert.Equal(string.Empty, roundTripped.Gemini);
        Assert.Equal(string.Empty, roundTripped.Gpt);
        Assert.True(roundTripped.IsAllEmpty);
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
    }
}
