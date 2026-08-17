using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.RpgMakerMV.Documents;

public sealed class MvIdArray<TView>
    where TView : MvNodeView
{
    private readonly MutableJsonProjectDocument _document;
    private readonly JsonPath _path;
    private readonly Func<MutableJsonProjectDocument, JsonPath, TView> _factory;

    public MvIdArray(
        MutableJsonProjectDocument document,
        JsonPath path,
        Func<MutableJsonProjectDocument, JsonPath, TView> factory)
    {
        _document = document;
        _path = path;
        _factory = factory;
    }

    public int PhysicalLength => ArrayNode.Elements.Count;

    public int HighestId => Math.Max(0, PhysicalLength - 1);

    public bool HasPhysicalSlot(int index) => index >= 0 && index < PhysicalLength;

    public bool IsNullSlot(int index) => HasPhysicalSlot(index)
        && ArrayNode.Elements[index].Kind == JsonSyntaxKind.Null;

    public TView? GetById(int id)
    {
        if (!HasPhysicalSlot(id) || IsNullSlot(id)) return null;
        return _factory(_document, _path.Append(id));
    }

    public IEnumerable<TView> Records()
    {
        for (int id = 1; id < PhysicalLength; id++)
        {
            TView? record = GetById(id);
            if (record is not null) yield return record;
        }
    }

    public void InsertRawAtId(int id, ReadOnlySpan<byte> rawJson)
    {
        if (id < 0 || id > PhysicalLength) throw new ArgumentOutOfRangeException(nameof(id));
        if (id < PhysicalLength)
        {
            if (!IsNullSlot(id)) throw new InvalidOperationException($"MV ID slot {id} is already occupied.");
            _document.SetRawJson(_path.Append(id), rawJson);
            return;
        }

        _document.InsertArrayElement(_path, id, rawJson);
    }

    public void InsertPhysicalSlot(int index, ReadOnlySpan<byte> rawJson)
    {
        if (index < 0 || index > PhysicalLength) throw new ArgumentOutOfRangeException(nameof(index));
        _document.InsertArrayElement(_path, index, rawJson);
    }

    public void SetRawAtId(int id, ReadOnlySpan<byte> rawJson)
    {
        if (!HasPhysicalSlot(id)) throw new ArgumentOutOfRangeException(nameof(id));
        _document.SetRawJson(_path.Append(id), rawJson);
    }

    public void SetNullAtId(int id)
    {
        if (!HasPhysicalSlot(id)) throw new ArgumentOutOfRangeException(nameof(id));
        _document.SetNull(_path.Append(id));
    }

    public void AppendNullSlots(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return;
        byte[][] values = Enumerable.Range(0, count).Select(_ => "null"u8.ToArray()).ToArray();
        _document.AppendArrayElements(_path, values);
    }

    private JsonArraySyntax ArrayNode => _document.Syntax.Resolve(_path) as JsonArraySyntax
        ?? throw new InvalidOperationException($"JSON path '{_path}' is not an array.");
}
