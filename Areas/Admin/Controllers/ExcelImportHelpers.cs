using System.Globalization;
using ClosedXML.Excel;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

// Shared row/column-reading helpers for the Master Data and Product Import tools (and the
// older simple Bulk Update tool has its own private copies of the same ideas).
internal static class ExcelImportHelpers
{
    public static Dictionary<string, int> MapHeaders(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in sheet.Row(1).CellsUsed())
        {
            var header = cell.GetString().Trim();
            if (!string.IsNullOrEmpty(header))
            {
                map[header] = cell.Address.ColumnNumber;
            }
        }
        return map;
    }

    public static int? FindColumn(Dictionary<string, int> headerMap, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (headerMap.TryGetValue(alias, out var col))
            {
                return col;
            }
        }
        return null;
    }

    public static string? GetString(IXLWorksheet sheet, int row, int? col)
    {
        if (col == null) return null;
        var value = sheet.Cell(row, col.Value).GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    public static bool TryReadDecimal(IXLWorksheet sheet, int row, int? col, out decimal value)
    {
        value = 0;
        if (col == null) return false;
        var cell = sheet.Cell(row, col.Value);
        if (cell.IsEmpty()) return false;
        if (cell.TryGetValue(out double d))
        {
            value = (decimal)d;
            return true;
        }
        return decimal.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryReadInt(IXLWorksheet sheet, int row, int? col, out int value)
    {
        value = 0;
        if (col == null) return false;
        var cell = sheet.Cell(row, col.Value);
        if (cell.IsEmpty()) return false;
        if (cell.TryGetValue(out double d))
        {
            value = (int)Math.Round(d);
            return true;
        }
        return int.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    // "Yes"/"No" (also tolerates True/False/1/0) — defaultValue when the cell is blank.
    public static bool ReadYesNo(IXLWorksheet sheet, int row, int? col, bool defaultValue)
    {
        var text = GetString(sheet, row, col);
        if (text == null) return defaultValue;
        return text.Equals("Yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("True", StringComparison.OrdinalIgnoreCase)
            || text == "1";
    }

    public static string Slugify(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        return slug.Trim('-');
    }

    public static string UniqueSlug(string name, HashSet<string> usedSlugs)
    {
        var slug = Slugify(name);
        if (string.IsNullOrEmpty(slug)) slug = "item";
        var candidate = slug;
        var suffix = 2;
        while (!usedSlugs.Add(candidate))
        {
            candidate = $"{slug}-{suffix}";
            suffix++;
        }
        return candidate;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var lenA = a.Length;
        var lenB = b.Length;
        var d = new int[lenA + 1, lenB + 1];
        for (var i = 0; i <= lenA; i++) d[i, 0] = i;
        for (var j = 0; j <= lenB; j++) d[0, j] = j;
        for (var i = 1; i <= lenA; i++)
        {
            for (var j = 1; j <= lenB; j++)
            {
                var cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[lenA, lenB];
    }

    // Suggests the closest known code/name for a "doesn't match anything" error — this is
    // specifically for hand-typed files, where the most common real mistakes are: the group
    // prefix left off entirely ("S" instead of "CL-S"), a case slip ("nav" vs "NAV" — already
    // matched case-insensitively before this ever gets called), and a typo'd value that's
    // otherwise close to a real one ("oli" vs "OLV"). Returns null rather than a misleading
    // guess when nothing is reasonably close.
    public static string? FindClosestMatch(string input, IEnumerable<string> candidates)
    {
        var candidateList = candidates as IReadOnlyCollection<string> ?? candidates.ToList();

        // "Missing prefix" case takes priority over raw edit distance: for a short bare value
        // like "S", an unrelated-but-short code (e.g. "OS") can have a smaller edit distance
        // than the actual intended match ("CL-S"), which would otherwise produce a misleading
        // suggestion. A candidate ending in "-<input>" is a much stronger, unambiguous signal.
        var suffixMatches = candidateList
            .Where(c => c.EndsWith("-" + input, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Length)
            .ToList();
        if (suffixMatches.Count > 0)
        {
            return suffixMatches[0];
        }

        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidateList)
        {
            var distance = LevenshteinDistance(input, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        // Loose cutoff: a suggestion only helps if it's clearly closer than "unrelated". Scales
        // with length so a 1-2 character code (e.g. a bare size like "S") can still match a
        // longer prefixed code (e.g. "CL-S") instead of being excluded by a small fixed cap.
        var threshold = Math.Max(3, best?.Length / 2 ?? 0);
        return bestDistance <= threshold ? best : null;
    }

    public static string WithSuggestion(string message, string? suggestion) =>
        suggestion == null ? message : $"{message} Did you mean '{suggestion}'?";
}
