namespace Playroma.Text.Generators.Names;

public static class NameSimilarity
{
    public static string Normalize(string value)
        => new((value ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static double Composite(string left, string right)
    {
        var a = Normalize(left); var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0) return a == b ? 1 : 0;
        if (a == b) return 1;
        return Math.Max(LevenshteinSimilarity(a, b), Math.Max(NGramJaccard(a, b, 2), LongestCommonSubstringRatio(a, b)));
    }

    public static double LevenshteinSimilarity(string left, string right)
    {
        if (left == right) return 1;
        if (left.Length == 0 || right.Length == 0) return 0;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return 1.0 - (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }

    public static double NGramJaccard(string left, string right, int n)
    {
        static HashSet<string> Grams(string value, int size)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (value.Length < size) { if (value.Length > 0) set.Add(value); return set; }
            for (var i = 0; i <= value.Length - size; i++) set.Add(value.Substring(i, size));
            return set;
        }
        var a = Grams(left, n); var b = Grams(right, n);
        if (a.Count == 0 && b.Count == 0) return 1;
        var intersection = a.Count(x => b.Contains(x));
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    public static double LongestCommonSubstringRatio(string left, string right)
    {
        var prev = new int[right.Length + 1];
        var best = 0;
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            for (var j = 1; j <= right.Length; j++)
                if (left[i - 1] == right[j - 1]) { current[j] = prev[j - 1] + 1; best = Math.Max(best, current[j]); }
            prev = current;
        }
        return (double)best / Math.Max(left.Length, right.Length);
    }
}
