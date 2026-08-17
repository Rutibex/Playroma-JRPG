namespace Playroma.Text.Generators.Tracery;

internal abstract record TracerySection(string Raw);
internal sealed record TraceryTextSection(string Text) : TracerySection(Text);
internal sealed record TraceryActionSection(string Action) : TracerySection(Action);
internal sealed record TraceryTagSection(
    string RawTag,
    string Symbol,
    IReadOnlyList<string> Modifiers,
    IReadOnlyList<string> PreActions,
    IReadOnlyList<string> PostActions) : TracerySection(RawTag);

internal sealed record TraceryParseResult(IReadOnlyList<TracerySection> Sections, IReadOnlyList<string> Errors);

internal static class TraceryParser
{
    public static TraceryParseResult ParseRule(string? rule, bool allowTopLevelActions = false)
    {
        rule ??= string.Empty;
        var sections = new List<TracerySection>();
        var errors = new List<string>();
        var text = new System.Text.StringBuilder();

        void FlushText()
        {
            if (text.Length == 0) return;
            sections.Add(new TraceryTextSection(text.ToString()));
            text.Clear();
        }

        for (var i = 0; i < rule.Length; i++)
        {
            var c = rule[i];
            if (c == '\\' && i + 1 < rule.Length && rule[i + 1] is '#' or '[' or ']' or '\\')
            {
                text.Append(rule[++i]);
                continue;
            }

            if (c == '#')
            {
                FlushText();
                var end = FindClosingHash(rule, i + 1);
                if (end < 0)
                {
                    errors.Add($"Odd number of '#' characters in rule '{rule}'.");
                    text.Append(rule.AsSpan(i));
                    break;
                }
                var rawTag = rule[(i + 1)..end];
                var tag = ParseTag(rawTag, errors);
                if (tag is not null) sections.Add(tag);
                i = end;
                continue;
            }

            // Classic tracery.js treats brackets outside #tags# as literal text.
            // Playroma Extended may opt into persistent top-level actions.
            if (allowTopLevelActions && c == '[')
            {
                FlushText();
                var end = FindMatchingBracket(rule, i);
                if (end < 0)
                {
                    errors.Add($"Too many '[' characters in rule '{rule}'.");
                    text.Append(rule.AsSpan(i));
                    break;
                }
                sections.Add(new TraceryActionSection(rule[(i + 1)..end]));
                i = end;
                continue;
            }

            if (allowTopLevelActions && c == ']')
            {
                errors.Add($"Too many ']' characters in rule '{rule}'.");
                text.Append(c);
                continue;
            }

            text.Append(c);
        }
        FlushText();
        return new TraceryParseResult(sections, errors);
    }

    internal static (TraceryTagSection? Tag, IReadOnlyList<string> Errors) ParseTagContent(string rawTag)
    {
        var errors = new List<string>();
        return (ParseTag(rawTag, errors), errors);
    }

    private static TraceryTagSection? ParseTag(string rawTag, List<string> errors)
    {
        var pre = new List<string>();
        var post = new List<string>();
        var plain = new System.Text.StringBuilder();
        var beforeSymbol = true;
        var sawPostAction = false;

        for (var i = 0; i < rawTag.Length; i++)
        {
            if (rawTag[i] == '[')
            {
                var end = FindMatchingBracket(rawTag, i);
                if (end < 0)
                {
                    errors.Add($"Too many '[' characters in tag '#{rawTag}#'.");
                    return null;
                }
                var action = rawTag[(i + 1)..end];
                if (plain.Length == 0 && beforeSymbol) pre.Add(action);
                else { post.Add(action); sawPostAction = true; }
                i = end;
                continue;
            }
            if (rawTag[i] == ']')
            {
                errors.Add($"Too many ']' characters in tag '#{rawTag}#'.");
                return null;
            }
            if (sawPostAction)
            {
                errors.Add($"Multiple possible expansion symbols in tag '#{rawTag}#'.");
                return null;
            }
            plain.Append(rawTag[i]);
            beforeSymbol = false;
        }

        var symbolAndMods = plain.ToString();
        if (string.IsNullOrWhiteSpace(symbolAndMods))
        {
            errors.Add($"Tag '#{rawTag}#' does not contain an expansion symbol.");
            return null;
        }
        var split = symbolAndMods.Split('.', StringSplitOptions.None);
        var symbol = split[0].Trim();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            errors.Add($"Tag '#{rawTag}#' has an empty expansion symbol.");
            return null;
        }
        return new TraceryTagSection(rawTag, symbol, split.Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(), pre, post);
    }

    private static int FindClosingHash(string text, int start)
    {
        var bracketDepth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '[') bracketDepth++;
            else if (text[i] == ']') bracketDepth--;
            else if (text[i] == '#' && bracketDepth == 0) return i;
        }
        return -1;
    }

    private static int FindMatchingBracket(string text, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '[') depth++;
            else if (text[i] == ']')
            {
                depth--;
                if (depth == 0) return i;
                if (depth < 0) return -1;
            }
        }
        return -1;
    }
}
