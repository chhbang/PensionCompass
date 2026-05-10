using PensionCompass.Core.Sync;

namespace PensionCompass.Core.Tests.Sync;

public class NoopSyncProviderTests
{
    [Fact]
    public void IsConfigured_AlwaysFalse()
    {
        Assert.False(NoopSyncProvider.Instance.IsConfigured);
    }

    [Fact]
    public void GetModifiedTime_AlwaysReturnsNull()
    {
        Assert.Null(NoopSyncProvider.Instance.GetModifiedTime("anything.json"));
    }

    [Fact]
    public void Read_AlwaysReturnsNull()
    {
        Assert.Null(NoopSyncProvider.Instance.Read("anything.json"));
    }

    [Fact]
    public void Write_DoesNotThrow_AndProducesNothing()
    {
        // No-op: writing should not throw, but reading right after should still find nothing.
        NoopSyncProvider.Instance.Write("anything.json", [1, 2, 3]);
        Assert.Null(NoopSyncProvider.Instance.Read("anything.json"));
    }

    [Fact]
    public void Delete_DoesNotThrow()
    {
        NoopSyncProvider.Instance.Delete("anything.json");
    }
}
