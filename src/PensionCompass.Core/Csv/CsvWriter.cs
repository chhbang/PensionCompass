using System.Text;
using PensionCompass.Core.Models;

namespace PensionCompass.Core.Csv;

/// <summary>
/// Writes the two CSV outputs defined in the spec (§3.1, §3.2).
/// Output is UTF-8 with BOM so Excel reads Korean text correctly. RFC 4180 quoting.
/// </summary>
public static class CsvWriter
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static void WritePrincipalGuaranteed(string path, IEnumerable<PrincipalGuaranteedProduct> products)
    {
        using var writer = new StreamWriter(path, append: false, Utf8WithBom);
        WriteRow(writer, ["운용사", "상품코드", "상품명", "금리", "만기기간"]);
        foreach (var p in products)
            WriteRow(writer, [p.AssetManager, p.ProductCode, p.ProductName, p.AppliedRate, p.MaturityTerm]);
    }

    public static void WriteFunds(string path, IEnumerable<FundProduct> funds, IReadOnlyList<ReturnPeriod> periods)
    {
        using var writer = new StreamWriter(path, append: false, Utf8WithBom);
        var header = new List<string> { "운용사", "상품코드", "상품명", "위험등급", "자산구분" };
        header.AddRange(periods.Select(p => p.ToCsvHeader()));
        WriteRow(writer, header);

        foreach (var fund in funds)
        {
            var row = new List<string>
            {
                fund.AssetManager,
                fund.ProductCode,
                fund.ProductName,
                fund.RiskGrade,
                fund.AssetClass,
            };
            row.AddRange(periods.Select(period => fund.Returns.TryGetValue(period, out var v) ? v : string.Empty));
            WriteRow(writer, row);
        }
    }

    private static void WriteRow(StreamWriter writer, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0) writer.Write(',');
            writer.Write(Quote(fields[i]));
        }
        writer.Write("\r\n");
    }

    private static string Quote(string field)
    {
        var needsQuoting = field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
        if (!needsQuoting) return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
