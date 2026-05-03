using System.Text;
using System.Text.RegularExpressions;
using PensionCompass.Core.Models;

namespace PensionCompass.Core.Csv;

/// <summary>
/// Reads the CSV pair produced by <see cref="CsvWriter"/> back into a <see cref="ProductCatalog"/>.
/// Useful when the user has previously exported and wants to skip re-parsing the source HTML.
/// Header columns are matched by name (lenient to extra columns and column order); required columns
/// must be present or the loader throws.
/// </summary>
public static class CsvCatalogLoader
{
    public const string PrincipalGuaranteedFileName = "원리금보장형_상품목록.csv";
    public const string FundsFileName = "펀드_상품목록.csv";

    private static readonly Regex ReturnPeriodColumnPattern = new(@"^수익률\(\s*([0-9]+(?:개월|년))\s*\)$", RegexOptions.Compiled);

    public static ProductCatalog Load(string folderPath)
    {
        var pgPath = Path.Combine(folderPath, PrincipalGuaranteedFileName);
        var fundPath = Path.Combine(folderPath, FundsFileName);

        var pgList = File.Exists(pgPath) ? LoadPrincipalGuaranteed(pgPath) : [];
        var (fundList, periods) = File.Exists(fundPath) ? LoadFunds(fundPath) : ([], []);

        return new ProductCatalog(pgList, fundList, periods);
    }

    public static List<PrincipalGuaranteedProduct> LoadPrincipalGuaranteed(string filePath)
    {
        var rows = ParseFile(filePath);
        if (rows.Count == 0) return [];

        var header = rows[0];
        var col = new ColumnLookup(header);
        col.Require("운용사", "상품코드", "상품명", "금리", "만기기간");

        var result = new List<PrincipalGuaranteedProduct>(rows.Count - 1);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (IsBlankRow(row)) continue;
            result.Add(new PrincipalGuaranteedProduct(
                ProductCode: col.Get(row, "상품코드"),
                ProductName: col.Get(row, "상품명"),
                AssetManager: col.Get(row, "운용사"),
                AppliedRate: col.Get(row, "금리"),
                MaturityTerm: col.Get(row, "만기기간")));
        }
        return result;
    }

    public static (List<FundProduct> Funds, List<ReturnPeriod> Periods) LoadFunds(string filePath)
    {
        var rows = ParseFile(filePath);
        if (rows.Count == 0) return ([], []);

        var header = rows[0];
        var col = new ColumnLookup(header);
        col.Require("운용사", "상품코드", "상품명", "위험등급");

        var periodColumns = new Dictionary<int, ReturnPeriod>();
        for (var c = 0; c < header.Count; c++)
        {
            var match = ReturnPeriodColumnPattern.Match(header[c]);
            if (!match.Success) continue;
            if (ReturnPeriodExtensions.TryParseFromKoreanLabel(match.Groups[1].Value, out var period))
                periodColumns[c] = period;
        }

        var funds = new List<FundProduct>(rows.Count - 1);
        var foundPeriods = new SortedSet<ReturnPeriod>();
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (IsBlankRow(row)) continue;

            var returns = new Dictionary<ReturnPeriod, string>();
            foreach (var (colIdx, period) in periodColumns)
            {
                if (colIdx >= row.Count) continue;
                var value = row[colIdx];
                if (string.IsNullOrEmpty(value)) continue;
                returns[period] = value;
                foundPeriods.Add(period);
            }

            funds.Add(new FundProduct(
                ProductCode: col.Get(row, "상품코드"),
                ProductName: col.Get(row, "상품명"),
                AssetManager: col.Get(row, "운용사"),
                RiskGrade: col.Get(row, "위험등급"),
                Returns: returns));
        }

        return (funds, foundPeriods.ToList());
    }

    private static bool IsBlankRow(IReadOnlyList<string> row)
        => row.Count == 0 || row.All(string.IsNullOrEmpty);

    private static List<List<string>> ParseFile(string filePath)
    {
        // detectEncodingFromByteOrderMarks handles the UTF-8 BOM that CsvWriter emits.
        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return ParseRfc4180(reader.ReadToEnd());
    }

    private static List<List<string>> ParseRfc4180(string text)
    {
        var rows = new List<List<string>>();
        var current = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuotes = false;
                    i++;
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
                i++;
                continue;
            }

            if (c == ',')
            {
                current.Add(field.ToString());
                field.Clear();
                i++;
                continue;
            }

            if (c == '\r')
            {
                i++;
                continue;
            }

            if (c == '\n')
            {
                current.Add(field.ToString());
                rows.Add(current);
                current = [];
                field.Clear();
                i++;
                continue;
            }

            field.Append(c);
            i++;
        }

        if (field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString());
            rows.Add(current);
        }

        return rows;
    }

    private sealed class ColumnLookup
    {
        private readonly Dictionary<string, int> _indexByName;

        public ColumnLookup(IReadOnlyList<string> header)
        {
            _indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < header.Count; i++)
                _indexByName[header[i].Trim()] = i;
        }

        public void Require(params string[] names)
        {
            var missing = names.Where(n => !_indexByName.ContainsKey(n)).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException($"CSV 헤더에 필수 컬럼이 없습니다: {string.Join(", ", missing)}");
        }

        public string Get(IReadOnlyList<string> row, string name)
        {
            if (!_indexByName.TryGetValue(name, out var idx)) return string.Empty;
            return idx < row.Count ? row[idx] : string.Empty;
        }
    }
}
