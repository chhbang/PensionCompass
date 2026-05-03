using System.Globalization;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PensionCompass.Core.Models;

namespace PensionCompass.Core.Pdf;

/// <summary>
/// Generates the rebalancing-proposal PDF via QuestPDF. The header, account summary,
/// and sell-decision table are built from <see cref="PdfReport"/> data; the AI's markdown
/// response is parsed via Markdig and walked into structured QuestPDF blocks (headings,
/// paragraphs, lists, tables, emphasis) so the PDF is properly formatted, not raw text.
/// </summary>
public static class PdfExporter
{
    private static readonly MarkdownPipeline MdPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private const string TitleColor = "#1F3864";
    private const string SubtitleColor = "#2E4D7C";
    private const string TableHeaderBg = "#F5F7FA";
    private const string TableBorder = "#D0D0D0";
    private const string CodeBg = "#F0F2F5";

    public static void Export(string filePath, PdfReport report)
    {
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(t => t.FontFamily("Malgun Gothic").FontSize(10).LineHeight(1.4f));

                page.Header().Element(c => Header(c, report));
                page.Content().Element(c => Content(c, report));
                page.Footer().AlignCenter().Text(t =>
                {
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf(filePath);
    }

    private static void Header(IContainer container, PdfReport report)
    {
        container.Column(col =>
        {
            col.Item().Text("삼성생명 IRP 리밸런싱 제안").FontSize(18).Bold().FontColor(TitleColor);
            col.Item().Text($"생성일시: {report.GeneratedAt:yyyy-MM-dd HH:mm} · {report.ProviderName} ({report.ModelId})")
                .FontSize(9).FontColor(Colors.Grey.Darken1);
            col.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor(TableBorder);
        });
    }

    private static void Content(IContainer container, PdfReport report)
    {
        container.PaddingVertical(8).Column(col =>
        {
            col.Item().Element(c => MetadataSection(c, report.Account));
            col.Item().PaddingTop(8).Element(c => AccountSummarySection(c, report.Account));
            col.Item().PaddingTop(8).Element(c => SellDecisionsSection(c, report.Account));

            col.Item().PaddingVertical(12).LineHorizontal(0.5f).LineColor(TableBorder);

            col.Item().Text("AI 제안").FontSize(14).Bold().FontColor(TitleColor);
            col.Item().PaddingTop(4).Element(c => RenderMarkdown(c, report.AiResponseMarkdown));
        });
    }

    private static void MetadataSection(IContainer container, AccountStatusModel account)
    {
        container.Column(col =>
        {
            col.Item().Text("리밸런싱 정보").FontSize(13).Bold().FontColor(SubtitleColor);
            col.Item().PaddingTop(2).Text(text =>
            {
                if (account.RebalanceTiming == RebalanceTiming.Immediate)
                {
                    text.Span("시점: ").Bold();
                    text.Span("최대한 즉시 일반 리밸런싱");
                }
                else
                {
                    text.Span("시점: ").Bold();
                    text.Span("만기 예약용 리밸런싱");
                    if (account.ExecutionDate is { } date)
                    {
                        text.Span(" · ");
                        text.Span("실행 예정일: ").Bold();
                        text.Span(date.ToString("yyyy년 M월 d일", CultureInfo.InvariantCulture));
                    }
                }
                text.Span(" · ");
                text.Span("주기: ").Bold();
                text.Span(account.RebalanceCycle switch
                {
                    RebalanceCycle.ThreeMonths => "3개월",
                    RebalanceCycle.SixMonths => "6개월",
                    RebalanceCycle.OneYear => "1년",
                    _ => "-",
                });
            });
        });
    }

    private static void AccountSummarySection(IContainer container, AccountStatusModel account)
    {
        container.Column(col =>
        {
            col.Item().Text("계좌 현황").FontSize(13).Bold().FontColor(SubtitleColor);
            col.Item().PaddingTop(2).Text(text =>
            {
                text.Span("총 적립금: ").Bold();
                text.Span(Won(account.TotalAmount));
                if (account.DepositAmount.HasValue)
                {
                    text.Span("   ");
                    text.Span("입금액: ").Bold();
                    text.Span(Won(account.DepositAmount.Value));
                }
                if (account.ProfitAmount.HasValue)
                {
                    text.Span("   ");
                    text.Span("운용수익: ").Bold();
                    text.Span(Won(account.ProfitAmount.Value));
                }
            });
        });
    }

    private static void SellDecisionsSection(IContainer container, AccountStatusModel account)
    {
        container.Column(col =>
        {
            col.Item().Text($"매도/유지 결정 ({account.OwnedItems.Count}개)").FontSize(13).Bold().FontColor(SubtitleColor);
            if (account.OwnedItems.Count == 0)
            {
                col.Item().PaddingTop(2).Text("(보유 상품 없음)").FontSize(9).FontColor(Colors.Grey.Darken1);
                return;
            }

            col.Item().PaddingTop(4).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(5);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });
                t.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text("상품명").Bold();
                    h.Cell().Element(HeaderCell).AlignRight().Text("적립금").Bold();
                    h.Cell().Element(HeaderCell).AlignCenter().Text("매도 정책").Bold();
                });

                foreach (var item in account.OwnedItems)
                {
                    t.Cell().Element(BodyCell).Text(item.ProductName);
                    t.Cell().Element(BodyCell).AlignRight().Text(Won(item.CurrentValue));
                    t.Cell().Element(BodyCell).AlignCenter().Text(item.IsSellable ? "매도 가능" : "매도 금지")
                        .FontColor(item.IsSellable ? Colors.Black : "#B22222");
                }
            });
        });

        static IContainer HeaderCell(IContainer c) => c.Background(TableHeaderBg).BorderBottom(0.5f).BorderColor(TableBorder).Padding(4);
        static IContainer BodyCell(IContainer c) => c.BorderBottom(0.3f).BorderColor(TableBorder).Padding(4);
    }

    private static void RenderMarkdown(IContainer container, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            container.Text("(응답 본문 없음)").FontColor(Colors.Grey.Darken1);
            return;
        }

        var doc = Markdig.Markdown.Parse(markdown, MdPipeline);
        container.Column(col =>
        {
            foreach (var block in doc)
                RenderBlock(col, block);
        });
    }

    private static void RenderBlock(ColumnDescriptor col, Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var size = heading.Level switch { 1 => 16f, 2 => 13f, 3 => 12f, _ => 11f };
                col.Item().PaddingTop(8).PaddingBottom(2).Text(t =>
                {
                    t.DefaultTextStyle(s => s.Bold().FontSize(size).FontColor(TitleColor));
                    RenderInlines(t, heading.Inline);
                });
                break;

            case ParagraphBlock para:
                col.Item().PaddingVertical(2).Text(t => RenderInlines(t, para.Inline));
                break;

            case ListBlock list:
                var idx = 1;
                foreach (var child in list)
                {
                    if (child is not ListItemBlock liBlock) continue;
                    var marker = list.IsOrdered ? $"{idx}." : "•";
                    col.Item().PaddingLeft(4).Row(row =>
                    {
                        row.ConstantItem(16).Text(marker);
                        row.RelativeItem().Column(inner =>
                        {
                            foreach (var sub in liBlock)
                                RenderBlock(inner, sub);
                        });
                    });
                    idx++;
                }
                break;

            case Table table:
                RenderTable(col, table);
                break;

            case FencedCodeBlock fenced:
                col.Item().PaddingVertical(4).Background(CodeBg).Padding(8).Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontFamily("Cascadia Code").FontSize(9));
                    t.Span(string.Join("\n", fenced.Lines.Lines.Select(l => l.ToString())));
                });
                break;

            case CodeBlock code:
                col.Item().PaddingVertical(4).Background(CodeBg).Padding(8).Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontFamily("Cascadia Code").FontSize(9));
                    t.Span(string.Join("\n", code.Lines.Lines.Select(l => l.ToString())));
                });
                break;

            case ThematicBreakBlock:
                col.Item().PaddingVertical(8).LineHorizontal(0.5f).LineColor(TableBorder);
                break;

            case QuoteBlock quote:
                col.Item().BorderLeft(2).BorderColor("#C0C0C0").PaddingLeft(10).Column(inner =>
                {
                    foreach (var sub in quote)
                        RenderBlock(inner, sub);
                });
                break;
        }
    }

    private static void RenderTable(ColumnDescriptor col, Table table)
    {
        var rows = table.OfType<TableRow>().ToList();
        if (rows.Count == 0) return;
        var columnCount = rows[0].Count;
        if (columnCount == 0) return;

        col.Item().PaddingVertical(4).Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                for (var i = 0; i < columnCount; i++)
                    c.RelativeColumn();
            });

            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                var isHeader = r == 0;
                foreach (var cellNode in row)
                {
                    if (cellNode is not TableCell cell) continue;
                    t.Cell().Element(c => CellContainer(c, isHeader)).Text(text =>
                    {
                        if (isHeader)
                            text.DefaultTextStyle(s => s.Bold());
                        foreach (var sub in cell)
                            if (sub is ParagraphBlock pb)
                                RenderInlines(text, pb.Inline);
                    });
                }
            }
        });

        static IContainer CellContainer(IContainer c, bool isHeader)
        {
            var cell = c.Border(0.3f).BorderColor(TableBorder).Padding(4);
            return isHeader ? cell.Background(TableHeaderBg) : cell;
        }
    }

    private static void RenderInlines(TextDescriptor text, ContainerInline? inline)
    {
        if (inline == null) return;
        foreach (var item in inline)
        {
            switch (item)
            {
                case LiteralInline lit:
                    text.Span(lit.Content.ToString());
                    break;
                case EmphasisInline emp:
                    var isBold = emp.DelimiterCount >= 2;
                    foreach (var child in emp)
                    {
                        if (child is LiteralInline cl)
                        {
                            var s = text.Span(cl.Content.ToString());
                            if (isBold) s.Bold();
                            else s.Italic();
                        }
                    }
                    break;
                case CodeInline ci:
                    text.Span(ci.Content).FontFamily("Cascadia Code").BackgroundColor(CodeBg);
                    break;
                case LinkInline link:
                    foreach (var child in link)
                        if (child is LiteralInline cl)
                            text.Span(cl.Content.ToString()).Underline().FontColor("#1F6FCC");
                    break;
                case LineBreakInline:
                    text.Span("\n");
                    break;
            }
        }
    }

    private static string Won(decimal amount)
        => "₩" + amount.ToString("N0", CultureInfo.InvariantCulture);
}
