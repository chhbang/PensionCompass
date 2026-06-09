using System.Text;
using PensionCompass.Core.Reference;

namespace PensionCompass.Core.Tests.Reference;

public sealed class ReferenceLibraryServiceTests : IDisposable
{
    private readonly string _baseDir;
    private readonly ReferenceLibraryService _sut;

    public ReferenceLibraryServiceTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "pc-reftests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);
        _sut = new ReferenceLibraryService(_baseDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_baseDir, recursive: true); } catch { }
    }

    private static byte[] Pdf(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void List_Empty_WhenNothingAdded()
    {
        Assert.Empty(_sut.List());
    }

    [Fact]
    public void Add_Then_List_RoundTripsMetadataAndBytes()
    {
        var content = Pdf("%PDF-1.7 fund guide");
        var doc = _sut.Add("미래에셋 가이드.pdf", content, ReferenceCategory.FundGuide);

        Assert.Equal("미래에셋 가이드.pdf", doc.FileName);
        Assert.Equal(ReferenceCategory.FundGuide, doc.Category);
        Assert.Equal(content.LongLength, doc.SizeBytes);
        Assert.True(doc.Enabled);

        var list = _sut.List();
        var only = Assert.Single(list);
        Assert.Equal(doc.Id, only.Id);
        Assert.Equal(content, _sut.ReadBytes(doc.Id));
    }

    [Fact]
    public void Add_PersistsAcrossNewServiceInstance()
    {
        var doc = _sut.Add("report.pdf", Pdf("x"), ReferenceCategory.MarketReport);
        var reopened = new ReferenceLibraryService(_baseDir);
        var only = Assert.Single(reopened.List());
        Assert.Equal(doc.Id, only.Id);
        Assert.Equal(ReferenceCategory.MarketReport, only.Category);
    }

    [Fact]
    public void SetEnabled_And_SetCategory_Mutate()
    {
        var doc = _sut.Add("a.pdf", Pdf("x"), ReferenceCategory.Other);

        _sut.SetEnabled(doc.Id, false);
        _sut.SetCategory(doc.Id, ReferenceCategory.MarketReport);

        var updated = Assert.Single(_sut.List());
        Assert.False(updated.Enabled);
        Assert.Equal(ReferenceCategory.MarketReport, updated.Category);
    }

    [Fact]
    public void Remove_DeletesEntryAndBytes()
    {
        var doc = _sut.Add("a.pdf", Pdf("x"), ReferenceCategory.Other);

        _sut.Remove(doc.Id);

        Assert.Empty(_sut.List());
        Assert.Null(_sut.ReadBytes(doc.Id));
    }

    [Fact]
    public void Add_SanitizesPathTraversalInFileName()
    {
        var doc = _sut.Add(@"..\..\evil.pdf", Pdf("x"), ReferenceCategory.Other);
        Assert.Equal("evil.pdf", doc.FileName);
    }

    [Fact]
    public void List_CorruptIndex_ReturnsEmptyRatherThanThrow()
    {
        Directory.CreateDirectory(Path.Combine(_baseDir, ReferenceLibraryService.FolderName));
        File.WriteAllText(Path.Combine(_baseDir, ReferenceLibraryService.FolderName, "references.json"), "not json {{{");

        Assert.Empty(_sut.List());
    }

    [Fact]
    public void ReadBytes_MissingId_ReturnsNull()
    {
        Assert.Null(_sut.ReadBytes("does-not-exist"));
    }

    [Fact]
    public void SetCloudSync_Persists()
    {
        var doc = _sut.Add("a.pdf", Pdf("x"), ReferenceCategory.FundGuide);
        Assert.False(doc.CloudSync);

        _sut.SetCloudSync(doc.Id, true);

        Assert.True(Assert.Single(_sut.List()).CloudSync);
    }

    [Fact]
    public void Import_PreservesIdAndContent_AndSkipsDuplicates()
    {
        var content = Pdf("cloud bytes");
        var incoming = new ReferenceDocument
        {
            Id = "fixed-cloud-id",
            FileName = "리포트.pdf",
            Category = ReferenceCategory.MarketReport,
            SizeBytes = content.LongLength,
            AddedUtc = DateTime.UtcNow,
            Enabled = false,
            CloudSync = true,
        };

        var imported = _sut.Import(incoming, content);

        Assert.NotNull(imported);
        Assert.Equal("fixed-cloud-id", imported!.Id);
        Assert.True(imported.Enabled);            // import forces Enabled on
        Assert.True(imported.CloudSync);
        Assert.Equal(content, _sut.ReadBytes("fixed-cloud-id"));

        // Importing the same id again is a no-op.
        Assert.Null(_sut.Import(incoming, content));
        Assert.Single(_sut.List());
    }

    [Fact]
    public void Metadata_SerializeDeserialize_RoundTrips()
    {
        var doc = new ReferenceDocument
        {
            Id = "id1",
            FileName = "미래에셋 가이드.pdf",
            Category = ReferenceCategory.FundGuide,
            SizeBytes = 12345,
            AddedUtc = new DateTime(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc),
            Enabled = true,
            CloudSync = true,
        };

        var bytes = ReferenceLibraryService.SerializeMetadata(doc);
        var back = ReferenceLibraryService.DeserializeMetadata(bytes);

        Assert.NotNull(back);
        Assert.Equal(doc.Id, back!.Id);
        Assert.Equal(doc.FileName, back.FileName);
        Assert.Equal(ReferenceCategory.FundGuide, back.Category);
        Assert.Equal(doc.SizeBytes, back.SizeBytes);
    }
}
