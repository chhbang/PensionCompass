using QuestPDF.Infrastructure;
using PensionCompass.Core.Models;
using PensionCompass.Core.Pdf;

namespace PensionCompass.Core.Tests;

public class PdfExporterTests : IDisposable
{
    private readonly string _tempPath;

    public PdfExporterTests()
    {
        // QuestPDF requires a license declaration before any document generation.
        QuestPDF.Settings.License = LicenseType.Community;
        _tempPath = Path.Combine(Path.GetTempPath(), $"PensionCompass_{Guid.NewGuid():N}.pdf");
    }

    public void Dispose()
    {
        try { File.Delete(_tempPath); } catch { /* best-effort */ }
    }

    [Fact]
    public void Export_WritesNonEmptyPdfWithKoreanContent()
    {
        var report = new PdfReport(
            GeneratedAt: new DateTime(2026, 5, 2, 14, 30, 0),
            ProviderName: "Gemini",
            ModelId: "gemini-2.5-pro",
            Account: new AccountStatusModel
            {
                TotalAmount = 192_808_229m,
                DepositAmount = 181_000_000m,
                ProfitAmount = 11_808_229m,
                RebalanceTiming = RebalanceTiming.Immediate,
                OwnedItems =
                [
                    new OwnedProductModel { ProductName = "이율보증형(3년)", CurrentValue = 30_646_260m, IsSellable = false },
                    new OwnedProductModel { ProductName = "[온라인]우리BIG2플러스증권(채권혼)ClassC-Pe", CurrentValue = 23_129_650m, IsSellable = true },
                ],
            },
            AiResponseMarkdown: """
                # 리밸런싱 제안

                ## 거시환경 진단
                현재 시점은 **변동성 장세**입니다.

                ## 매도 후보
                - 채권혼합형 일부 매도
                - *기존 펀드 비중 조정*

                | 상품 | 비중 |
                |---|---|
                | 인컴 펀드 | 40% |
                | 채권혼합 | 30% |
                """);

        PdfExporter.Export(_tempPath, report);

        Assert.True(File.Exists(_tempPath));
        var bytes = File.ReadAllBytes(_tempPath);
        Assert.True(bytes.Length > 1000, "PDF should contain meaningful content (> 1KB).");
        // Quick sanity check: PDF files start with the %PDF magic bytes.
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    [Fact]
    public void Export_HandlesEmptyHoldingsAndEmptyMarkdown()
    {
        var report = new PdfReport(
            GeneratedAt: DateTime.Now,
            ProviderName: "Claude",
            ModelId: "claude-opus-4-7",
            Account: new AccountStatusModel { TotalAmount = 100_000m },
            AiResponseMarkdown: "");

        PdfExporter.Export(_tempPath, report);

        Assert.True(File.Exists(_tempPath));
        Assert.True(new FileInfo(_tempPath).Length > 0);
    }
}
