using System.Globalization;
using System.Text.RegularExpressions;

namespace Playroma.JRPG.PluginTags.Yanfly;

internal sealed class YanflyVersionComparer : IComparer<string?>
{
    public static YanflyVersionComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        int[] left = ParseParts(x);
        int[] right = ParseParts(y);
        int length = Math.Max(left.Length, right.Length);
        for (int index = 0; index < length; index++)
        {
            int leftPart = index < left.Length ? left[index] : 0;
            int rightPart = index < right.Length ? right[index] : 0;
            int comparison = leftPart.CompareTo(rightPart);
            if (comparison != 0) return comparison;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(x ?? string.Empty, y ?? string.Empty);
    }

    private static int[] ParseParts(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [0];
        MatchCollection matches = Regex.Matches(value, "\\d+", RegexOptions.CultureInvariant);
        if (matches.Count == 0) return [0];
        return matches
            .Cast<Match>()
            .Select(match => int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int part) ? part : 0)
            .ToArray();
    }
}
