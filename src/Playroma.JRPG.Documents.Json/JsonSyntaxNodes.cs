using System.Text;

namespace Playroma.JRPG.Documents.Json;

public enum JsonSyntaxKind
{
    Object,
    Array,
    Property,
    String,
    Number,
    True,
    False,
    Null
}

public abstract class JsonSyntaxNode
{
    protected JsonSyntaxNode(JsonSyntaxKind kind, TextSpan span)
    {
        Kind = kind;
        Span = span;
    }

    public JsonSyntaxKind Kind { get; }

    public TextSpan Span { get; }

    public JsonSyntaxNode? Parent { get; internal set; }

    public ReadOnlyMemory<byte> GetSource(ReadOnlyMemory<byte> source) => source.Slice(Span.Start, Span.Length);
}

public sealed class JsonObjectSyntax : JsonSyntaxNode
{
    public JsonObjectSyntax(
        TextSpan span,
        int openBraceOffset,
        int closeBraceOffset,
        IReadOnlyList<JsonPropertySyntax> properties)
        : base(JsonSyntaxKind.Object, span)
    {
        OpenBraceOffset = openBraceOffset;
        CloseBraceOffset = closeBraceOffset;
        Properties = properties;
        foreach (JsonPropertySyntax property in properties)
        {
            property.Parent = this;
        }
    }

    public int OpenBraceOffset { get; }

    public int CloseBraceOffset { get; }

    public IReadOnlyList<JsonPropertySyntax> Properties { get; }

    public JsonPropertySyntax? FindLastProperty(string name) => Properties.LastOrDefault(
        property => string.Equals(property.Name, name, StringComparison.Ordinal));
}

public sealed class JsonArraySyntax : JsonSyntaxNode
{
    public JsonArraySyntax(
        TextSpan span,
        int openBracketOffset,
        int closeBracketOffset,
        IReadOnlyList<JsonSyntaxNode> elements)
        : base(JsonSyntaxKind.Array, span)
    {
        OpenBracketOffset = openBracketOffset;
        CloseBracketOffset = closeBracketOffset;
        Elements = elements;
        foreach (JsonSyntaxNode element in elements)
        {
            element.Parent = this;
        }
    }

    public int OpenBracketOffset { get; }

    public int CloseBracketOffset { get; }

    public IReadOnlyList<JsonSyntaxNode> Elements { get; }
}

public sealed class JsonPropertySyntax : JsonSyntaxNode
{
    public JsonPropertySyntax(TextSpan span, JsonStringSyntax nameToken, JsonSyntaxNode value)
        : base(JsonSyntaxKind.Property, span)
    {
        NameToken = nameToken;
        Name = nameToken.Value;
        Value = value;
        nameToken.Parent = this;
        value.Parent = this;
    }

    public string Name { get; }

    public JsonStringSyntax NameToken { get; }

    public JsonSyntaxNode Value { get; }
}

public sealed class JsonStringSyntax : JsonSyntaxNode
{
    public JsonStringSyntax(TextSpan span, string value)
        : base(JsonSyntaxKind.String, span)
    {
        Value = value;
    }

    public string Value { get; }
}

public sealed class JsonNumberSyntax : JsonSyntaxNode
{
    public JsonNumberSyntax(TextSpan span, string rawText)
        : base(JsonSyntaxKind.Number, span)
    {
        RawText = rawText;
    }

    public string RawText { get; }
}

public sealed class JsonLiteralSyntax : JsonSyntaxNode
{
    public JsonLiteralSyntax(JsonSyntaxKind kind, TextSpan span)
        : base(kind, span)
    {
        if (kind is not JsonSyntaxKind.True and not JsonSyntaxKind.False and not JsonSyntaxKind.Null)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
