namespace Playroma.JRPG.PluginTags;

public enum PluginTextNodeKind
{
    RawText,
    BlankLine,
    RecognizedTag,
    RecognizedBlock,
    ActionSequenceBlock,
    ScriptBlock,
    UnknownTag,
    MalformedTag
}

public abstract class PluginTextNode
{
    protected PluginTextNode(PluginTextNodeKind kind, PluginTextSpan span, string rawText)
    {
        Kind = kind;
        Span = span;
        RawText = rawText ?? throw new ArgumentNullException(nameof(rawText));
    }

    public PluginTextNodeKind Kind { get; }
    public PluginTextSpan Span { get; }
    public string RawText { get; }
}

public sealed class RawTextNode : PluginTextNode
{
    public RawTextNode(PluginTextSpan span, string rawText) : base(PluginTextNodeKind.RawText, span, rawText)
    {
    }
}

public sealed class BlankLineNode : PluginTextNode
{
    public BlankLineNode(PluginTextSpan span, string rawText) : base(PluginTextNodeKind.BlankLine, span, rawText)
    {
    }
}

public sealed class RecognizedTagNode : PluginTextNode
{
    public RecognizedTagNode(
        PluginTextSpan span,
        string rawText,
        PluginFeatureDefinition definition,
        PluginFeatureValues values)
        : base(PluginTextNodeKind.RecognizedTag, span, rawText)
    {
        Definition = definition;
        Values = values;
    }

    public PluginFeatureDefinition Definition { get; }
    public PluginFeatureValues Values { get; }
}

public class RecognizedBlockNode : PluginTextNode
{
    public RecognizedBlockNode(
        PluginTextNodeKind kind,
        PluginTextSpan span,
        string rawText,
        PluginFeatureDefinition definition,
        PluginFeatureValues values,
        string openingText,
        string bodyText,
        string closingText)
        : base(kind, span, rawText)
    {
        Definition = definition;
        Values = values;
        OpeningText = openingText;
        BodyText = bodyText;
        ClosingText = closingText;
    }

    public PluginFeatureDefinition Definition { get; }
    public PluginFeatureValues Values { get; }
    public string OpeningText { get; }
    public string BodyText { get; }
    public string ClosingText { get; }
}

public sealed class ActionSequenceBlockNode : RecognizedBlockNode
{
    public ActionSequenceBlockNode(
        PluginTextSpan span,
        string rawText,
        PluginFeatureDefinition definition,
        PluginFeatureValues values,
        string openingText,
        string bodyText,
        string closingText)
        : base(PluginTextNodeKind.ActionSequenceBlock, span, rawText, definition, values, openingText, bodyText, closingText)
    {
    }
}

public sealed class ScriptBlockNode : RecognizedBlockNode
{
    public ScriptBlockNode(
        PluginTextSpan span,
        string rawText,
        PluginFeatureDefinition definition,
        PluginFeatureValues values,
        string openingText,
        string bodyText,
        string closingText)
        : base(PluginTextNodeKind.ScriptBlock, span, rawText, definition, values, openingText, bodyText, closingText)
    {
    }
}

public sealed class UnknownTagNode : PluginTextNode
{
    public UnknownTagNode(PluginTextSpan span, string rawText, string tagText)
        : base(PluginTextNodeKind.UnknownTag, span, rawText)
    {
        TagText = tagText;
    }

    public string TagText { get; }
}

public sealed class MalformedTagNode : PluginTextNode
{
    public MalformedTagNode(
        PluginTextSpan span,
        string rawText,
        string issueCode,
        string message,
        PluginFeatureDefinition? expectedDefinition = null)
        : base(PluginTextNodeKind.MalformedTag, span, rawText)
    {
        IssueCode = issueCode;
        Message = message;
        ExpectedDefinition = expectedDefinition;
    }

    public string IssueCode { get; }
    public string Message { get; }
    public PluginFeatureDefinition? ExpectedDefinition { get; }
}

public sealed class PluginTextDocument
{
    public PluginTextDocument(string sourceText, PluginTagContext context, IReadOnlyList<PluginTextNode> nodes)
    {
        SourceText = sourceText ?? throw new ArgumentNullException(nameof(sourceText));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        ValidateLosslessCoverage(SourceText, Nodes);
    }

    public string SourceText { get; }
    public PluginTagContext Context { get; }
    public IReadOnlyList<PluginTextNode> Nodes { get; }

    public IEnumerable<RecognizedTagNode> RecognizedTags => Nodes.OfType<RecognizedTagNode>();
    public IEnumerable<RecognizedBlockNode> RecognizedBlocks => Nodes.OfType<RecognizedBlockNode>();
    public IEnumerable<PluginTextNode> RecognizedNodes => Nodes.Where(node => node is RecognizedTagNode or RecognizedBlockNode);

    private static void ValidateLosslessCoverage(string sourceText, IReadOnlyList<PluginTextNode> nodes)
    {
        int expectedStart = 0;
        foreach (PluginTextNode node in nodes)
        {
            if (node.Span.Start != expectedStart)
                throw new ArgumentException($"Plugin text nodes are not contiguous at offset {expectedStart}.", nameof(nodes));
            if (node.Span.Length != node.RawText.Length)
                throw new ArgumentException("A plugin text node span does not match its raw-text length.", nameof(nodes));
            if (node.Span.End > sourceText.Length ||
                !sourceText.AsSpan(node.Span.Start, node.Span.Length).SequenceEqual(node.RawText.AsSpan()))
            {
                throw new ArgumentException("A plugin text node does not reproduce its source span exactly.", nameof(nodes));
            }
            expectedStart = node.Span.End;
        }

        if (expectedStart != sourceText.Length)
            throw new ArgumentException("Plugin text nodes do not cover the complete source text.", nameof(nodes));
    }

    public IReadOnlyList<PluginTextNode> Find(PluginFeatureKey key) => Nodes.Where(node => node switch
    {
        RecognizedTagNode tag => tag.Definition.Key == key,
        RecognizedBlockNode block => block.Definition.Key == key,
        _ => false
    }).ToArray();
}
