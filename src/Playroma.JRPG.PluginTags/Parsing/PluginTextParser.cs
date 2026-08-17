using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Playroma.JRPG.PluginTags;

public sealed class PluginTextParser
{
    private readonly PluginTagCatalog _catalog;
    private readonly ConcurrentDictionary<PluginFeatureKey, CompiledFeature> _compiled = new();

    public PluginTextParser(PluginTagCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public PluginTextDocument Parse(string? text, PluginTagContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string source = text ?? string.Empty;
        IReadOnlyList<TextLine> lines = SplitLines(source);
        List<PluginTextNode> nodes = [];
        IReadOnlyList<PluginFeatureDefinition> candidates = _catalog.ForSurface(context.Surface);

        int lineIndex = 0;
        while (lineIndex < lines.Count)
        {
            TextLine line = lines[lineIndex];
            string trimmed = line.Content.Trim();
            if (trimmed.Length == 0)
            {
                nodes.Add(new BlankLineNode(new PluginTextSpan(line.Start, line.Raw.Length), line.Raw));
                lineIndex++;
                continue;
            }

            if (TryMatchBlockOpening(line.Content, candidates, out PluginFeatureDefinition blockDefinition, out PluginFeatureValues openingValues))
            {
                CompiledFeature compiled = GetCompiled(blockDefinition);
                BlockSearchResult search = FindClosingLine(lines, lineIndex + 1, compiled);
                if (search.ClosingIndex < 0)
                {
                    int length = source.Length - line.Start;
                    nodes.Add(new MalformedTagNode(
                        new PluginTextSpan(line.Start, length),
                        source.Substring(line.Start, length),
                        "missing-closing-tag",
                        "The block has no matching closing tag.",
                        blockDefinition));
                    break;
                }

                TextLine closing = lines[search.ClosingIndex];
                int end = closing.Start + closing.Raw.Length;
                string raw = source.Substring(line.Start, end - line.Start);
                if (search.NestedOpeningIndex >= 0)
                {
                    nodes.Add(new MalformedTagNode(
                        new PluginTextSpan(line.Start, raw.Length),
                        raw,
                        "nested-block",
                        "The block contains a nested opening tag of the same type.",
                        blockDefinition));
                    lineIndex = search.ClosingIndex + 1;
                    continue;
                }

                int bodyStart = line.Start + line.Raw.Length;
                string body = source.Substring(bodyStart, closing.Start - bodyStart);
                PluginFeatureValues blockValues = new(openingValues.Arguments, body);
                PluginTextSpan span = new(line.Start, raw.Length);
                PluginTextNode node = CreateBlockNode(blockDefinition, span, raw, blockValues, line.Raw, body, closing.Raw);
                nodes.Add(node);
                lineIndex = search.ClosingIndex + 1;
                continue;
            }

            if (TryMatchSingleLine(line.Content, candidates, out PluginFeatureDefinition definition, out PluginFeatureValues values))
            {
                nodes.Add(new RecognizedTagNode(new PluginTextSpan(line.Start, line.Raw.Length), line.Raw, definition, values));
                lineIndex++;
                continue;
            }

            if (TryMatchUnexpectedClosing(line.Content, candidates, out PluginFeatureDefinition closingDefinition))
            {
                nodes.Add(new MalformedTagNode(
                    new PluginTextSpan(line.Start, line.Raw.Length),
                    line.Raw,
                    "unexpected-closing-tag",
                    "A closing tag appears without a matching opening tag.",
                    closingDefinition));
                lineIndex++;
                continue;
            }

            if (LooksLikeTag(trimmed))
                nodes.Add(new UnknownTagNode(new PluginTextSpan(line.Start, line.Raw.Length), line.Raw, trimmed));
            else
                nodes.Add(new RawTextNode(new PluginTextSpan(line.Start, line.Raw.Length), line.Raw));
            lineIndex++;
        }

        return new PluginTextDocument(source, context, nodes);
    }

    private bool TryMatchBlockOpening(
        string line,
        IEnumerable<PluginFeatureDefinition> candidates,
        out PluginFeatureDefinition definition,
        out PluginFeatureValues values)
    {
        foreach (PluginFeatureDefinition candidate in candidates.Where(feature => feature.Syntax.IsBlock))
        {
            CompiledFeature compiled = GetCompiled(candidate);
            foreach (CompiledPattern pattern in compiled.OpeningPatterns)
            {
                if (!pattern.TryMatch(line, out Match match)) continue;
                definition = candidate;
                values = ValuesFromMatch(pattern.Regex, match);
                return true;
            }
        }

        definition = null!;
        values = null!;
        return false;
    }

    private bool TryMatchSingleLine(
        string line,
        IEnumerable<PluginFeatureDefinition> candidates,
        out PluginFeatureDefinition definition,
        out PluginFeatureValues values)
    {
        foreach (PluginFeatureDefinition candidate in candidates.Where(feature => !feature.Syntax.IsBlock))
        {
            CompiledFeature compiled = GetCompiled(candidate);
            foreach (CompiledPattern pattern in compiled.OpeningPatterns)
            {
                if (!pattern.TryMatch(line, out Match match)) continue;
                definition = candidate;
                values = ValuesFromMatch(pattern.Regex, match);
                return true;
            }
        }

        definition = null!;
        values = null!;
        return false;
    }

    private bool TryMatchUnexpectedClosing(
        string line,
        IEnumerable<PluginFeatureDefinition> candidates,
        out PluginFeatureDefinition definition)
    {
        foreach (PluginFeatureDefinition candidate in candidates.Where(feature => feature.Syntax.IsBlock))
        {
            CompiledFeature compiled = GetCompiled(candidate);
            if (compiled.ClosingPatterns.Any(pattern => pattern.IsMatch(line)))
            {
                definition = candidate;
                return true;
            }
        }

        definition = null!;
        return false;
    }

    private static BlockSearchResult FindClosingLine(IReadOnlyList<TextLine> lines, int start, CompiledFeature compiled)
    {
        int depth = 0;
        int nestedOpeningIndex = -1;
        for (int index = start; index < lines.Count; index++)
        {
            string line = lines[index].Content;
            bool isOpening = compiled.OpeningPatterns.Any(pattern => pattern.IsMatch(line));
            if (isOpening)
            {
                depth++;
                if (nestedOpeningIndex < 0) nestedOpeningIndex = index;
                continue;
            }

            bool isClosing = compiled.ClosingPatterns.Any(pattern => pattern.IsMatch(line));
            if (!isClosing) continue;
            if (depth == 0) return new BlockSearchResult(index, nestedOpeningIndex);
            depth--;
        }

        return new BlockSearchResult(-1, nestedOpeningIndex);
    }

    private static PluginTextNode CreateBlockNode(
        PluginFeatureDefinition definition,
        PluginTextSpan span,
        string raw,
        PluginFeatureValues values,
        string opening,
        string body,
        string closing)
    {
        if (definition.Surface == PluginAuthoringSurface.ActionSequence || definition.Syntax.Kind == PluginSyntaxKind.OrderedBlock)
            return new ActionSequenceBlockNode(span, raw, definition, values, opening, body, closing);
        if (definition.Syntax.Kind == PluginSyntaxKind.ScriptBlock || definition.Complexity == PluginFeatureComplexity.AdvancedCode)
            return new ScriptBlockNode(span, raw, definition, values, opening, body, closing);
        return new RecognizedBlockNode(PluginTextNodeKind.RecognizedBlock, span, raw, definition, values, opening, body, closing);
    }

    private CompiledFeature GetCompiled(PluginFeatureDefinition definition) =>
        _compiled.GetOrAdd(definition.Key, _ => new CompiledFeature(
            definition.Syntax.Patterns.Select(CompilePattern).ToArray(),
            (definition.Syntax.ClosingPatterns ?? []).Select(CompilePattern).ToArray()));

    private static CompiledPattern CompilePattern(PluginSyntaxPattern pattern)
    {
        RegexOptions options = RegexOptions.CultureInvariant;
        if (pattern.IgnoreCase) options |= RegexOptions.IgnoreCase;
        Regex regex = new(pattern.RegularExpression, options, TimeSpan.FromMilliseconds(250));
        return new CompiledPattern(regex, pattern.MatchTrimmedLine);
    }

    private static PluginFeatureValues ValuesFromMatch(Regex regex, Match match)
    {
        List<KeyValuePair<string, PluginValue>> values = [];
        foreach (string groupName in regex.GetGroupNames())
        {
            if (int.TryParse(groupName, out _)) continue;
            Group group = match.Groups[groupName];
            if (group.Success) values.Add(new KeyValuePair<string, PluginValue>(groupName, PluginValue.FromText(group.Value)));
        }
        return new PluginFeatureValues(values);
    }

    private static bool LooksLikeTag(string text) =>
        text.Length >= 3 && text[0] == '<' && text[^1] == '>';

    private static IReadOnlyList<TextLine> SplitLines(string text)
    {
        if (text.Length == 0) return [];
        List<TextLine> lines = [];
        int index = 0;
        while (index < text.Length)
        {
            int start = index;
            while (index < text.Length && text[index] is not '\r' and not '\n') index++;
            int contentEnd = index;
            if (index < text.Length)
            {
                if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index += 2;
                else index++;
            }

            string raw = text.Substring(start, index - start);
            string content = text.Substring(start, contentEnd - start);
            lines.Add(new TextLine(start, content, raw));
        }

        return lines;
    }

    private sealed record TextLine(int Start, string Content, string Raw);
    private sealed record BlockSearchResult(int ClosingIndex, int NestedOpeningIndex);
    private sealed record CompiledFeature(IReadOnlyList<CompiledPattern> OpeningPatterns, IReadOnlyList<CompiledPattern> ClosingPatterns);
    private sealed record CompiledPattern(Regex Regex, bool MatchTrimmedLine)
    {
        public string SelectInput(string line) => MatchTrimmedLine ? line.Trim() : line;

        public bool TryMatch(string line, out Match match)
        {
            try
            {
                match = Regex.Match(SelectInput(line));
                return match.Success;
            }
            catch (RegexMatchTimeoutException)
            {
                match = Match.Empty;
                return false;
            }
        }

        public bool IsMatch(string line)
        {
            try
            {
                return Regex.IsMatch(SelectInput(line));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
