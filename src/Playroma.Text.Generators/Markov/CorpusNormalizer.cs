using System.Text;
using System.Text.RegularExpressions;
using Playroma.Text.Core.Corpora;

namespace Playroma.Text.Generators.Markov;

public static partial class CorpusNormalizer
{
    [GeneratedRegex(@"[\t ]+")]
    private static partial Regex HorizontalWhitespace();

    public static string Normalize(string text, CorpusNormalizationOptions? options = null)
    {
        options ??= new CorpusNormalizationOptions();
        text ??= string.Empty;
        if (options.NormalizeUnicode) text = text.Normalize(NormalizationForm.FormKC);
        if (options.NormalizeNewlines) text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        var lines = text.Split('\n');
        var result = new List<string>(lines.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in lines)
        {
            var line = source;
            if (options.CollapseWhitespace) line = HorizontalWhitespace().Replace(line, " ");
            if (options.TrimLines) line = line.Trim();
            if (options.Lowercase) line = line.ToLowerInvariant();
            if (options.RemoveEmptyLines && line.Length == 0) continue;
            if (options.RemoveDuplicateLines && !seen.Add(line)) continue;
            result.Add(line);
        }
        return string.Join("\n", result);
    }
}
