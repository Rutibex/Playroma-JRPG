using System.Text;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.RpgMakerMV.Documents;

public class MvRawArrayView
{
    protected readonly MutableJsonProjectDocument Document;
    protected readonly JsonPath Path;

    public MvRawArrayView(MutableJsonProjectDocument document, JsonPath path)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Path = path;
    }

    public int Count => ArrayNode.Elements.Count;

    public JsonSyntaxKind GetKind(int index) => GetNode(index).Kind;

    public string GetRaw(int index)
    {
        LosslessJsonDocument syntax = Document.Syntax;
        JsonSyntaxNode node = GetNode(index, syntax);
        return Encoding.UTF8.GetString(node.GetSource(syntax.OriginalBytes).Span);
    }

    public int? GetInt32(int index) => Document.GetInt32(Path.Append(index));

    public string? GetString(int index) => Document.GetString(Path.Append(index));

    public bool? GetBoolean(int index) => Document.GetBoolean(Path.Append(index));

    public void SetRaw(int index, ReadOnlySpan<byte> rawJson) => Document.SetRawJson(Path.Append(index), rawJson);

    public void SetInt32(int index, int value) => Document.SetInt32(Path.Append(index), value);

    public void SetString(int index, string value) => Document.SetString(Path.Append(index), value);

    public void SetBoolean(int index, bool value) => Document.SetBoolean(Path.Append(index), value);

    public void InsertRaw(int index, ReadOnlySpan<byte> rawJson) => Document.InsertArrayElement(Path, index, rawJson);

    public void AppendRaw(params byte[][] values) => Document.AppendArrayElements(Path, values);

    public void RemoveAt(int index) => Document.RemoveArrayElement(Path, index);

    protected JsonArraySyntax ArrayNode => Document.Syntax.Resolve(Path) as JsonArraySyntax
        ?? throw new InvalidOperationException($"JSON path '{Path}' is not an array.");

    protected JsonSyntaxNode GetNode(int index, LosslessJsonDocument? syntax = null)
    {
        JsonArraySyntax array = syntax?.Resolve(Path) as JsonArraySyntax ?? ArrayNode;
        if (index < 0 || index >= array.Elements.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return array.Elements[index];
    }
}

public sealed class MvArrayView<TView> : MvRawArrayView
    where TView : MvNodeView
{
    private readonly Func<MutableJsonProjectDocument, JsonPath, TView> _factory;

    public MvArrayView(
        MutableJsonProjectDocument document,
        JsonPath path,
        Func<MutableJsonProjectDocument, JsonPath, TView> factory)
        : base(document, path)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public TView? Get(int index)
    {
        JsonSyntaxNode node = GetNode(index);
        return node.Kind == JsonSyntaxKind.Null ? null : _factory(Document, Path.Append(index));
    }

    public IEnumerable<TView> Items()
    {
        for (int index = 0; index < Count; index++)
        {
            TView? item = Get(index);
            if (item is not null) yield return item;
        }
    }
}
