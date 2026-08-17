using System.Text;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.RpgMakerMV.Documents;

public abstract class MvNodeView
{
    protected MvNodeView(MutableJsonProjectDocument document, JsonPath path)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Path = path;
    }

    protected MutableJsonProjectDocument Document { get; }

    public JsonPath Path { get; }

    public JsonSyntaxNode? RawNode => Document.Syntax.Resolve(Path);

    protected string GetString(string property, string defaultValue = "") =>
        Document.GetString(Path.Append(property)) ?? defaultValue;

    protected int GetInt32(string property, int defaultValue = 0) =>
        Document.GetInt32(Path.Append(property)) ?? defaultValue;

    protected double GetDouble(string property, double defaultValue = 0) =>
        Document.GetDouble(Path.Append(property)) ?? defaultValue;

    protected bool GetBoolean(string property, bool defaultValue = false) =>
        Document.GetBoolean(Path.Append(property)) ?? defaultValue;

    protected void SetString(string property, string value)
    {
        JsonPath target = Path.Append(property);
        if (Document.Syntax.Resolve(target) is null)
        {
            Document.AddProperty(Path, property, JsonValueEncoder.EncodeString(value));
        }
        else
        {
            Document.SetString(target, value);
        }
    }

    protected void SetInt32(string property, int value)
    {
        JsonPath target = Path.Append(property);
        if (Document.Syntax.Resolve(target) is null)
        {
            Document.AddProperty(Path, property, JsonValueEncoder.EncodeInt32(value));
        }
        else
        {
            Document.SetInt32(target, value);
        }
    }

    protected void SetDouble(string property, double value)
    {
        JsonPath target = Path.Append(property);
        if (Document.Syntax.Resolve(target) is null)
        {
            Document.AddProperty(Path, property, JsonValueEncoder.EncodeDouble(value));
        }
        else
        {
            Document.SetDouble(target, value);
        }
    }

    protected void SetBoolean(string property, bool value)
    {
        JsonPath target = Path.Append(property);
        if (Document.Syntax.Resolve(target) is null)
        {
            Document.AddProperty(Path, property, JsonValueEncoder.EncodeBoolean(value));
        }
        else
        {
            Document.SetBoolean(target, value);
        }
    }

    protected bool HasProperty(string property) => Document.Syntax.Resolve(Path.Append(property)) is not null;

    protected MvRawArrayView GetRawArray(string property) => new(Document, Path.Append(property));

    protected MvArrayView<TView> GetObjectArray<TView>(
        string property,
        Func<MutableJsonProjectDocument, JsonPath, TView> factory)
        where TView : MvNodeView => new(Document, Path.Append(property), factory);

    protected string GetRaw(string property, string defaultValue = "null")
    {
        LosslessJsonDocument syntax = Document.Syntax;
        JsonSyntaxNode? node = syntax.Resolve(Path.Append(property));
        return node is null ? defaultValue : Encoding.UTF8.GetString(node.GetSource(syntax.OriginalBytes).Span);
    }

    protected void SetRaw(string property, string rawJson)
    {
        JsonPath target = Path.Append(property);
        byte[] bytes = Encoding.UTF8.GetBytes(rawJson);
        if (Document.Syntax.Resolve(target) is null)
        {
            Document.AddProperty(Path, property, bytes);
        }
        else
        {
            Document.SetRawJson(target, bytes);
        }
    }
}

public abstract class MvRecordView : MvNodeView
{
    protected MvRecordView(MutableJsonProjectDocument document, JsonPath path)
        : base(document, path)
    {
    }

    public int Id
    {
        get => GetInt32("id");
        set => SetInt32("id", value);
    }

    public string Name
    {
        get => GetString("name");
        set => SetString("name", value);
    }

    public string Note
    {
        get => GetString("note");
        set => SetString("note", value);
    }
}
