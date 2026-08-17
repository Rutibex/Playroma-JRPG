using System.Globalization;
using System.Text.RegularExpressions;

namespace Playroma.JRPG.PluginTags;

public sealed record PluginTagReference(
    PluginReferenceKind Kind,
    int Id,
    PluginFeatureKey Feature,
    string ArgumentName,
    PluginTextSpan SourceSpan);

public sealed class PluginTagReferenceExtractor
{
    private const int MaximumExpandedRange = 10_000;

    private static readonly Regex IntegerPattern = new(
        "(?<![A-Za-z0-9_])-?[0-9]+",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    public IReadOnlyList<PluginTagReference> Extract(PluginTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<PluginTagReference> references = [];
        HashSet<(PluginReferenceKind Kind, int Id, PluginFeatureKey Feature, string Argument, int Start)> seen = new();

        foreach (PluginTextNode node in document.Nodes)
        {
            PluginFeatureDefinition? definition = null;
            PluginFeatureValues? values = null;
            switch (node)
            {
                case RecognizedTagNode tag:
                    definition = tag.Definition;
                    values = tag.Values;
                    break;
                case RecognizedBlockNode block:
                    definition = block.Definition;
                    values = block.Values;
                    break;
            }

            if (definition is null || values is null) continue;
            foreach (PluginArgumentDefinition argument in definition.Arguments.Where(argument => argument.ReferenceKind != PluginReferenceKind.None))
            {
                if (!values.TryGetValue(argument.Name, out PluginValue value)) continue;
                foreach (int id in ExtractIds(argument, value.Text))
                {
                    var identity = (argument.ReferenceKind, id, definition.Key, argument.Name, node.Span.Start);
                    if (!seen.Add(identity)) continue;
                    references.Add(new PluginTagReference(argument.ReferenceKind, id, definition.Key, argument.Name, node.Span));
                }
            }
        }

        return references;
    }

    private static IEnumerable<int> ExtractIds(PluginArgumentDefinition argument, string text)
    {
        int[] values = IntegerPattern.Matches(text)
            .Cast<Match>()
            .Select(match => int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : -1)
            .Where(id => id >= 0)
            .ToArray();

        if (values.Length == 0) yield break;
        if (argument.Kind == PluginArgumentKind.DatabaseIdRange && values.Length >= 2)
        {
            int first = values[0];
            int last = values[1];
            int distance = Math.Abs(last - first);
            if (distance <= MaximumExpandedRange)
            {
                int step = first <= last ? 1 : -1;
                for (int id = first; ; id += step)
                {
                    yield return id;
                    if (id == last) break;
                }
                yield break;
            }
        }

        if (!argument.AllowMultiple && argument.Kind is not PluginArgumentKind.DatabaseIdList)
        {
            yield return values[0];
            yield break;
        }

        foreach (int id in values) yield return id;
    }
}
