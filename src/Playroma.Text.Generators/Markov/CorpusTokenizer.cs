using System.Text.RegularExpressions;
using Playroma.Text.Core.Corpora;

namespace Playroma.Text.Generators.Markov;

public static partial class CorpusTokenizer
{
    public const string SentenceBoundaryToken = "\u0004<SENTENCE>";
    [GeneratedRegex(@"[\p{L}\p{M}\p{N}'’\-]+|[^\s]")]
    private static partial Regex WordTokenRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[\p{L}\p{N}""'“‘])")]
    private static partial Regex SentenceBoundaryRegex();

    public static IReadOnlyList<IReadOnlyList<string>> TokenizeSequences(string normalizedText, CorpusTokenMode mode, bool includePunctuation = true)
    {
        normalizedText ??= string.Empty;
        return mode switch
        {
            CorpusTokenMode.Character => normalizedText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => (IReadOnlyList<string>)x.Select(ch => ch.ToString()).ToArray()).Where(x => x.Count > 0).ToArray(),
            CorpusTokenMode.Word => SplitSentences(normalizedText).Select(sentence => (IReadOnlyList<string>)TokenizeWords(sentence, includePunctuation)).Where(x => x.Count > 0).ToArray(),
            CorpusTokenMode.Sentence => normalizedText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(paragraph => (IReadOnlyList<string>)TokenizeParagraph(paragraph, includePunctuation)).Where(x => x.Count > 0).ToArray(),
            _ => Array.Empty<IReadOnlyList<string>>()
        };
    }

    public static IReadOnlyList<string> TokenizeWords(string value, bool includePunctuation = true)
    {
        var tokens = WordTokenRegex().Matches(value ?? string.Empty).Select(x => x.Value);
        if (!includePunctuation) tokens = tokens.Where(x => x.Any(char.IsLetterOrDigit));
        return tokens.ToArray();
    }

    public static IReadOnlyList<string> SplitSentences(string value)
        => SentenceBoundaryRegex().Split((value ?? string.Empty).Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();

    public static string Detokenize(IEnumerable<string> tokens, CorpusTokenMode mode)
    {
        var list = tokens.Where(x => x is not null).ToList();
        if (mode == CorpusTokenMode.Character) return string.Concat(list);
        if (mode == CorpusTokenMode.Sentence) list = list.Where(x => x != SentenceBoundaryToken).ToList();

        var output = new System.Text.StringBuilder();
        string? previous = null;
        foreach (var token in list)
        {
            if (output.Length > 0 && NeedsSpace(previous, token)) output.Append(' ');
            output.Append(token);
            previous = token;
        }
        return output.ToString().Trim();
    }

    private static IReadOnlyList<string> TokenizeParagraph(string paragraph, bool includePunctuation)
    {
        var result = new List<string>();
        var sentences = SplitSentences(paragraph);
        for (var i = 0; i < sentences.Count; i++)
        {
            if (i > 0) result.Add(SentenceBoundaryToken);
            result.AddRange(TokenizeWords(sentences[i], includePunctuation));
        }
        return result;
    }

    private static bool NeedsSpace(string? previous, string token)
    {
        if (string.IsNullOrEmpty(previous)) return false;
        if (token.Length == 1 && ",.;:!?)]}%\"”’".Contains(token[0])) return false;
        if (previous.Length == 1 && "([{\"“‘".Contains(previous[0])) return false;
        return true;
    }
}
