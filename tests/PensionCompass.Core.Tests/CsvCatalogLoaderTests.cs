using PensionCompass.Core.Csv;
using PensionCompass.Core.Models;

namespace PensionCompass.Core.Tests;

public class CsvCatalogLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public CsvCatalogLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PensionCompass_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Load_RoundTripsWriterOutput()
    {
        var pg = new List<PrincipalGuaranteedProduct>
        {
            new("C02011", "무배당 메리츠 신탁제공용 이율보증형보험Ⅲ (IRP 3년)", "메리츠", "3.80%", "3년"),
            new("G02003", "이율보증형(3년)", "삼성생명", "3.65%", "3년"),
        };
        var funds = new List<FundProduct>
        {
            new("G04783", "[온라인전용]미래에셋코어테크증권투자신탁(주식)", "미래에셋자산운용㈜", "매우높은위험",
                new Dictionary<ReturnPeriod, string> { [ReturnPeriod.Month3] = "45.00%" }),
            new("G99999", "테스트, 쉼표 포함 \"따옴표\"도", "테스트운용", "보통위험",
                new Dictionary<ReturnPeriod, string>
                {
                    [ReturnPeriod.Month3] = "5.00%",
                    [ReturnPeriod.Year1] = "12.00%",
                }),
        };
        var periods = new List<ReturnPeriod> { ReturnPeriod.Month1, ReturnPeriod.Month3, ReturnPeriod.Month6, ReturnPeriod.Year1, ReturnPeriod.Year3 };

        CsvWriter.WritePrincipalGuaranteed(Path.Combine(_tempDir, CsvCatalogLoader.PrincipalGuaranteedFileName), pg);
        CsvWriter.WriteFunds(Path.Combine(_tempDir, CsvCatalogLoader.FundsFileName), funds, periods);

        var loaded = CsvCatalogLoader.Load(_tempDir);

        Assert.Equal(2, loaded.PrincipalGuaranteed.Count);
        Assert.Equal(pg[0], loaded.PrincipalGuaranteed[0]);
        Assert.Equal(pg[1], loaded.PrincipalGuaranteed[1]);

        Assert.Equal(2, loaded.Funds.Count);
        var miraeAsset = loaded.Funds.Single(f => f.ProductCode == "G04783");
        Assert.Equal("미래에셋자산운용㈜", miraeAsset.AssetManager);
        Assert.Equal("매우높은위험", miraeAsset.RiskGrade);
        Assert.Single(miraeAsset.Returns);
        Assert.Equal("45.00%", miraeAsset.Returns[ReturnPeriod.Month3]);

        var roundTrip = loaded.Funds.Single(f => f.ProductCode == "G99999");
        Assert.Equal("테스트, 쉼표 포함 \"따옴표\"도", roundTrip.ProductName);
        Assert.Equal(2, roundTrip.Returns.Count);
        Assert.Equal("5.00%", roundTrip.Returns[ReturnPeriod.Month3]);
        Assert.Equal("12.00%", roundTrip.Returns[ReturnPeriod.Year1]);

        Assert.Contains(ReturnPeriod.Month3, loaded.FundReturnPeriods);
        Assert.Contains(ReturnPeriod.Year1, loaded.FundReturnPeriods);
    }

    [Fact]
    public void Load_ReturnsEmptyWhenFolderHasNoCsvs()
    {
        var loaded = CsvCatalogLoader.Load(_tempDir);

        Assert.Empty(loaded.PrincipalGuaranteed);
        Assert.Empty(loaded.Funds);
        Assert.Empty(loaded.FundReturnPeriods);
    }

    [Fact]
    public void Load_HandlesPartialFolder_FundsCsvOnly()
    {
        var funds = new List<FundProduct>
        {
            new("G04783", "[온라인전용]미래에셋코어테크증권투자신탁(주식)", "미래에셋자산운용㈜", "매우높은위험",
                new Dictionary<ReturnPeriod, string> { [ReturnPeriod.Month3] = "45.00%" }),
        };
        CsvWriter.WriteFunds(Path.Combine(_tempDir, CsvCatalogLoader.FundsFileName), funds, [ReturnPeriod.Month3]);

        var loaded = CsvCatalogLoader.Load(_tempDir);

        Assert.Empty(loaded.PrincipalGuaranteed);
        Assert.Single(loaded.Funds);
        Assert.Equal([ReturnPeriod.Month3], loaded.FundReturnPeriods);
    }

    [Fact]
    public void LoadPrincipalGuaranteed_ThrowsOnMissingRequiredColumn()
    {
        var path = Path.Combine(_tempDir, CsvCatalogLoader.PrincipalGuaranteedFileName);
        File.WriteAllText(path, "운용사,상품코드,상품명\r\n메리츠,C02011,Some\r\n");

        Assert.Throws<InvalidDataException>(() => CsvCatalogLoader.LoadPrincipalGuaranteed(path));
    }
}
