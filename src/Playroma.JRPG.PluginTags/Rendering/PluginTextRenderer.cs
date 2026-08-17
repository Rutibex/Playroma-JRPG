using System.Text;
using System.Text.RegularExpressions;

namespace Playroma.JRPG.PluginTags;

public sealed class PluginTagRenderException : Exception
{
    public PluginTagRenderException(string message) : base(message)
    {
    }
}

public sealed class PluginTextRenderer
{
    private static readonly Regex PlaceholderPattern = new(
        "\\{(?<name>[A-Za-z][A-Za-z0-9_]*)\\}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    public string RenderDocument(PluginTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        StringBuilder builder = new(document.SourceText.Length);
        foreach (PluginTextNode node in document.Nodes) builder.Append(node.RawText);
        return builder.ToString();
    }

    public string RenderFeature(
        PluginFeatureDefinition definition,
        PluginFeatureValues values,
        string lineEnding = "\n")
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);
        if (lineEnding is not "\n" and not "\r\n" and not "\r")
            throw new ArgumentException("Line ending must be LF, CRLF, or CR.", nameof(lineEnding));

        string opening = RenderTemplate(definition.Syntax.CanonicalTemplate, values);
        if (!definition.Syntax.IsBlock) return opening;
        if (string.IsNullOrWhiteSpace(definition.Syntax.ClosingTemplate))
            throw new PluginTagRenderException("Block feature has no closing template: " + definition.Key);

        string closing = RenderTemplate(definition.Syntax.ClosingTemplate, values);
        string body = values.Body ?? string.Empty;
        StringBuilder builder = new(opening.Length + closing.Length + body.Length + 8);
        builder.Append(opening);
        builder.Append(lineEnding);
        if (body.Length > 0)
        {
            builder.Append(body);
            if (!EndsWithLineEnding(body)) builder.Append(lineEnding);
        }
        builder.Append(closing);
        return builder.ToString();
    }

    public string RenderTemplate(string template, PluginFeatureValues values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        return PlaceholderPattern.Replace(template, match =>
        {
            string name = match.Groups["name"].Value;
            if (!values.TryGetValue(name, out PluginValue value))
                throw new PluginTagRenderException("Missing value for placeholder {" + name + "}.");
            return value.Text;
        });
    }

    private static bool EndsWithLineEnding(string value) =>
        value.EndsWith("\n", StringComparison.Ordinal) || value.EndsWith("\r", StringComparison.Ordinal);
}
