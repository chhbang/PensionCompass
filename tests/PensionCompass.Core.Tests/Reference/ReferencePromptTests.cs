using PensionCompass.Core.Ai;
using PensionCompass.Core.Models;
using PensionCompass.Core.Reference;

namespace PensionCompass.Core.Tests.Reference;

public class ReferencePromptTests
{
    private static ReferenceDocument Doc(string name, ReferenceCategory cat, bool enabled = true)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            FileName = name,
            Category = cat,
            SizeBytes = 1,
            AddedUtc = DateTime.UtcNow,
            Enabled = enabled,
        };

    private static PromptInput InputWith(params ReferenceDocument[] refs)
        => new(
            Catalog: null,
            Account: new AccountStatusModel { TotalAmount = 100_000_000m },
            UserAdditionalQuery: "",
            References: refs);

    [Fact]
    public void Build_WithReferences_IncludesSectionWithNamesAndCategoryFraming()
    {
        var output = PromptBuilder.Build(InputWith(
            Doc("미래에셋 가이드.pdf", ReferenceCategory.FundGuide),
            Doc("대신 리포트.pdf", ReferenceCategory.MarketReport)));

        Assert.Contains("## 참고 자료", output.UserPrompt);
        Assert.Contains("미래에셋 가이드.pdf", output.UserPrompt);
        Assert.Contains("대신 리포트.pdf", output.UserPrompt);
        Assert.Contains(ReferenceCategory.FundGuide.ToKoreanLabel(), output.UserPrompt);
        Assert.Contains(ReferenceCategory.MarketReport.ToKoreanLabel(), output.UserPrompt);
        Assert.Contains("시점에 의존", output.UserPrompt); // MarketReport framing leaked through
    }

    [Fact]
    public void Build_ExcludesDisabledReferences()
    {
        var output = PromptBuilder.Build(InputWith(
            Doc("enabled.pdf", ReferenceCategory.FundGuide, enabled: true),
            Doc("disabled.pdf", ReferenceCategory.Other, enabled: false)));

        Assert.Contains("enabled.pdf", output.UserPrompt);
        Assert.DoesNotContain("disabled.pdf", output.UserPrompt);
        Assert.Contains("첨부 PDF 1개", output.UserPrompt);
    }

    [Fact]
    public void Build_NoReferences_OmitsSection()
    {
        Assert.DoesNotContain("## 참고 자료", PromptBuilder.Build(InputWith()).UserPrompt);
    }

    [Fact]
    public void EveryCategory_HasLabelAndFraming()
    {
        foreach (var c in Enum.GetValues<ReferenceCategory>())
        {
            Assert.False(string.IsNullOrWhiteSpace(c.ToKoreanLabel()));
            Assert.False(string.IsNullOrWhiteSpace(c.ToLlmFraming()));
        }
    }
}
