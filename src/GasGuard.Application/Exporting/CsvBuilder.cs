using System.Text;

namespace GasGuard.Application.Exporting;

public static class CsvBuilder
{
    public static string Build(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(Escape)));
        }

        return builder.ToString();
    }

    public static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (IsSpreadsheetFormulaStart(text))
        {
            text = "'" + text;
        }

        if (text.Contains('"') || text.Contains(',') || text.Contains('\r') || text.Contains('\n'))
        {
            return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return text;
    }

    private static bool IsSpreadsheetFormulaStart(string value)
    {
        var trimmed = value.TrimStart(' ', '\t');
        if (trimmed.Length == 0)
        {
            return false;
        }

        return trimmed[0] is '=' or '+' or '-' or '@' or '\r' or '\n';
    }
}
