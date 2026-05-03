using PensionCompass.Core.Markdown;

namespace PensionCompass.Core.Tests;

public class MarkdownToHtmlTests
{
    [Fact]
    public void Convert_RendersHeadingsAsHtml()
    {
        var html = MarkdownToHtml.Convert("# 거시환경 진단\n\n본문 내용입니다.");

        Assert.Contains("<h1", html);
        Assert.Contains("거시환경 진단", html);
        Assert.Contains("본문 내용입니다", html);
    }

    [Fact]
    public void Convert_RendersGfmTables()
    {
        var md = """
            | 운용사 | 비중 |
            |---|---|
            | 삼성생명 | 40% |
            | 미래에셋 | 30% |
            """;

        var html = MarkdownToHtml.Convert(md);

        Assert.Contains("<table>", html);
        Assert.Contains("<th>", html);
        Assert.Contains("삼성생명", html);
        Assert.Contains("40%", html);
    }

    [Fact]
    public void Convert_EmbedsKoreanFontsInCss()
    {
        var html = MarkdownToHtml.Convert("내용");

        Assert.Contains("Malgun Gothic", html);
    }

    [Fact]
    public void Convert_EmptyOrNullInput_ReturnsEmptyBody()
    {
        var html = MarkdownToHtml.Convert(string.Empty);

        Assert.Contains("<body>", html);
        Assert.Contains("</body>", html);
    }
}
