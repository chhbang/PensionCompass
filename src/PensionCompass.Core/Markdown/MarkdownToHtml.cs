using Markdig;

namespace PensionCompass.Core.Markdown;

/// <summary>
/// Renders the AI's markdown response into a self-contained HTML document for WebView2 display.
/// Pipeline enables tables and other GFM extensions so multi-column responses (which the AI tends
/// to produce for portfolio recommendations) render correctly. The CSS lives in this file as a
/// constant to keep the converter pure (no resource-loading I/O).
/// </summary>
public static class MarkdownToHtml
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string Convert(string markdown)
    {
        var bodyHtml = Markdig.Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        return BuildHtmlDocument(bodyHtml);
    }

    private static string BuildHtmlDocument(string bodyHtml)
        => """
        <!DOCTYPE html>
        <html lang="ko">
        <head>
            <meta charset="UTF-8">
            <style>
                body {
                    font-family: 'Segoe UI', 'Malgun Gothic', 'Apple SD Gothic Neo', sans-serif;
                    line-height: 1.65;
                    padding: 16px 24px;
                    color: #1f1f1f;
                    background: transparent;
                }
                h1 { font-size: 1.6em; margin-top: 0.8em; color: #1f3864; border-bottom: 1px solid #e0e0e0; padding-bottom: 0.2em; }
                h2 { font-size: 1.3em; margin-top: 1.2em; color: #1f3864; }
                h3 { font-size: 1.1em; margin-top: 1em; color: #2e4d7c; }
                p { margin: 0.5em 0; }
                ul, ol { margin: 0.5em 0; padding-left: 1.6em; }
                li { margin: 0.2em 0; }
                strong { color: #1f3864; }
                em { color: #555; }
                table { border-collapse: collapse; margin: 1em 0; font-size: 0.95em; }
                th, td { border: 1px solid #d0d0d0; padding: 6px 12px; text-align: left; }
                th { background: #f5f7fa; font-weight: 600; }
                tr:nth-child(even) td { background: #fafbfc; }
                code { background: #f0f2f5; padding: 1px 5px; border-radius: 3px; font-family: 'Cascadia Code', Consolas, monospace; font-size: 0.92em; }
                pre { background: #f5f7fa; padding: 12px; border-radius: 4px; overflow-x: auto; }
                pre code { background: transparent; padding: 0; }
                blockquote { border-left: 3px solid #c0c0c0; padding-left: 12px; color: #555; margin: 0.8em 0; }
                hr { border: none; border-top: 1px solid #e0e0e0; margin: 1.4em 0; }
            </style>
        </head>
        <body>
        """ + bodyHtml + """
        </body>
        </html>
        """;
}
