using System.Text;
using PensionCompass.Core.Sync;

namespace PensionCompass.Core.Tests.Sync;

public class PreferenceSyncServiceTests
{
    [Fact]
    public void UploadDownload_RoundTrips()
    {
        var provider = new InMemorySyncProvider();
        var sut = new PreferenceSyncService();
        var original = new PreferenceSyncService.PreferenceBundle(
            AiProvider: "Gemini",
            ClaudeModel: "claude-opus-4-7",
            GeminiModel: "gemini-3.1-pro-preview",
            GptModel: "gpt-5",
            ThinkingLevel: "High");

        sut.Upload(provider, original);
        var roundTripped = sut.Download(provider);

        Assert.NotNull(roundTripped);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void Download_Missing_ReturnsNull()
        => Assert.Null(new PreferenceSyncService().Download(new InMemorySyncProvider()));

    [Fact]
    public void Download_Corrupt_ReturnsNull()
    {
        var provider = new InMemorySyncProvider();
        provider.Write("settings.json", Encoding.UTF8.GetBytes("not json {{{"));
        Assert.Null(new PreferenceSyncService().Download(provider));
    }

    [Fact]
    public void Download_PartialBundle_DefaultsMissingToEmpty()
    {
        var provider = new InMemorySyncProvider();
        provider.Write("settings.json", Encoding.UTF8.GetBytes("""{"aiProvider":"GPT"}"""));

        var bundle = new PreferenceSyncService().Download(provider);

        Assert.NotNull(bundle);
        Assert.Equal("GPT", bundle!.AiProvider);
        Assert.Equal(string.Empty, bundle.ClaudeModel);
        Assert.Equal(string.Empty, bundle.ThinkingLevel);
    }

    private sealed class InMemorySyncProvider : ISyncProvider
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool IsConfigured => true;
        public DateTime? GetModifiedTime(string fileName) => _store.ContainsKey(fileName) ? DateTime.UtcNow : null;
        public byte[]? Read(string fileName) => _store.TryGetValue(fileName, out var v) ? v : null;
        public void Write(string fileName, byte[] content) => _store[fileName] = content;
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
