using System.Globalization;

namespace GasGuard.Infrastructure.Modem;

public static class AtResponseParsing
{
    public static IReadOnlyList<string> ParseCommaSeparatedFields(string line)
    {
        var colonIndex = line.IndexOf(':');
        var payload = colonIndex >= 0 ? line[(colonIndex + 1)..] : line;
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < payload.Length; i++)
        {
            var character = payload[i];
            if (character == '"')
            {
                if (inQuotes && i + 1 < payload.Length && payload[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (character == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }

    public static int? ParseStorageIndex(string line)
    {
        var fields = ParseCommaSeparatedFields(line);
        if (fields.Count == 0)
        {
            return null;
        }

        return int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? index
            : null;
    }

    public static string? ParseSender(string line)
    {
        var fields = ParseCommaSeparatedFields(line);
        return fields.Count switch
        {
            >= 3 => NullIfEmpty(fields[2]),
            >= 1 => NullIfEmpty(fields[0]),
            _ => null
        };
    }

    public static string? ParseCmtSender(string line)
    {
        var fields = ParseCommaSeparatedFields(line);
        return fields.Count > 0 ? NullIfEmpty(fields[0]) : null;
    }

    public static string? ParseCmtiIndex(string line)
    {
        var fields = ParseCommaSeparatedFields(line);
        if (fields.Count < 2)
        {
            return null;
        }

        return int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? index.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    public static string? ParseCmgsReference(IEnumerable<string> lines) =>
        lines
            .Where(x => x.StartsWith("+CMGS:", StringComparison.OrdinalIgnoreCase))
            .Select(x => x["+CMGS:".Length..].Trim())
            .FirstOrDefault(x => x.Length > 0);

    public static string? ParseQuotedValue(string line, int fieldIndex)
    {
        var fields = ParseCommaSeparatedFields(line);
        return fields.Count > fieldIndex ? NullIfEmpty(fields[fieldIndex]) : null;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
