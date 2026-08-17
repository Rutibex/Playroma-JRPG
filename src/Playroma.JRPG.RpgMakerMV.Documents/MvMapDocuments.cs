using System.Text.Json;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.RpgMakerMV.Documents;

public sealed class MvSystemView : MvNodeView
{
    public MvSystemView(MutableJsonProjectDocument document) : base(document, JsonPath.Root) { }
    public string GameTitle { get => GetString("gameTitle"); set => SetString("gameTitle", value); }
    public string CurrencyUnit { get => GetString("currencyUnit"); set => SetString("currencyUnit", value); }
    public int StartMapId { get => GetInt32("startMapId"); set => SetInt32("startMapId", value); }
    public int StartX { get => GetInt32("startX"); set => SetInt32("startX", value); }
    public int StartY { get => GetInt32("startY"); set => SetInt32("startY", value); }
    public MvRawArrayView Elements => GetRawArray("elements");
    public MvRawArrayView SkillTypes => GetRawArray("skillTypes");
    public MvRawArrayView WeaponTypes => GetRawArray("weaponTypes");
    public MvRawArrayView ArmorTypes => GetRawArray("armorTypes");
    public MvRawArrayView EquipmentTypes => GetRawArray("equipTypes");
    public MvRawArrayView Switches => GetRawArray("switches");
    public MvRawArrayView Variables => GetRawArray("variables");
    public MvVehicleView Airship => new(Document, Path.Append("airship"));
    public MvVehicleView Boat => new(Document, Path.Append("boat"));
    public MvVehicleView Ship => new(Document, Path.Append("ship"));
}

public sealed class MvMapInfoView : MvNodeView
{
    public MvMapInfoView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Id { get => GetInt32("id"); set => SetInt32("id", value); }
    public string Name { get => GetString("name"); set => SetString("name", value); }
    public int ParentId { get => GetInt32("parentId"); set => SetInt32("parentId", value); }
    public int Order { get => GetInt32("order"); set => SetInt32("order", value); }
    public bool Expanded { get => GetBoolean("expanded"); set => SetBoolean("expanded", value); }
}

public sealed class MvEventCommandView : MvNodeView
{
    public MvEventCommandView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Code { get => GetInt32("code"); set => SetInt32("code", value); }
    public int Indent { get => GetInt32("indent"); set => SetInt32("indent", value); }
    public string ParametersRawJson => GetRaw("parameters", "[]");
    public MvRawArrayView Parameters => GetRawArray("parameters");
}

public sealed class MvEventView : MvNodeView
{
    public MvEventView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Id { get => GetInt32("id"); set => SetInt32("id", value); }
    public string Name { get => GetString("name"); set => SetString("name", value); }
    public int X { get => GetInt32("x"); set => SetInt32("x", value); }
    public int Y { get => GetInt32("y"); set => SetInt32("y", value); }
    public MvArrayView<MvEventPageView> Pages => GetObjectArray("pages", static (document, path) => new MvEventPageView(document, path));
}

public sealed class MvMapView : MvNodeView
{
    private MvMapDataBuffer? _data;

    public MvMapView(MutableJsonProjectDocument document) : base(document, JsonPath.Root) { }
    public string DisplayName { get => GetString("displayName"); set => SetString("displayName", value); }
    public int Width
    {
        get => GetInt32("width");
        set
        {
            EnsureMapDataCanBeInvalidated();
            SetInt32("width", value);
            _data = null;
        }
    }

    public int Height
    {
        get => GetInt32("height");
        set
        {
            EnsureMapDataCanBeInvalidated();
            SetInt32("height", value);
            _data = null;
        }
    }
    public int TilesetId { get => GetInt32("tilesetId"); set => SetInt32("tilesetId", value); }
    public string Note { get => GetString("note"); set => SetString("note", value); }
    public int EncounterStep { get => GetInt32("encounterStep", 30); set => SetInt32("encounterStep", value); }
    public int ScrollType { get => GetInt32("scrollType"); set => SetInt32("scrollType", value); }
    public bool DisableDash { get => GetBoolean("disableDashing"); set => SetBoolean("disableDashing", value); }
    public bool AutoplayBgm { get => GetBoolean("autoplayBgm"); set => SetBoolean("autoplayBgm", value); }
    public bool AutoplayBgs { get => GetBoolean("autoplayBgs"); set => SetBoolean("autoplayBgs", value); }
    public MvAudioFileView BackgroundMusic => new(Document, Path.Append("bgm"));
    public MvAudioFileView BackgroundSound => new(Document, Path.Append("bgs"));
    public string Battleback1Name { get => GetString("battleback1Name"); set => SetString("battleback1Name", value); }
    public string Battleback2Name { get => GetString("battleback2Name"); set => SetString("battleback2Name", value); }
    public string ParallaxName { get => GetString("parallaxName"); set => SetString("parallaxName", value); }
    public bool ParallaxLoopX { get => GetBoolean("parallaxLoopX"); set => SetBoolean("parallaxLoopX", value); }
    public bool ParallaxLoopY { get => GetBoolean("parallaxLoopY"); set => SetBoolean("parallaxLoopY", value); }
    public int ParallaxSpeedX { get => GetInt32("parallaxSx"); set => SetInt32("parallaxSx", value); }
    public int ParallaxSpeedY { get => GetInt32("parallaxSy"); set => SetInt32("parallaxSy", value); }
    public bool ParallaxShowZero { get => GetBoolean("parallaxShow"); set => SetBoolean("parallaxShow", value); }
    public MvArrayView<MvMapEncounterView> Encounters => GetObjectArray("encounterList", static (document, path) => new MvMapEncounterView(document, path));
    public MvIdArray<MvEventView> Events => new(Document, Path.Append("events"), static (document, path) => new MvEventView(document, path));
    public MvMapDataBuffer Data => _data ??= MvMapDataBuffer.Load(Document, Width, Height);

    private void EnsureMapDataCanBeInvalidated()
    {
        if (_data?.IsDirty == true)
        {
            throw new InvalidOperationException(
                "Commit or discard pending map tile changes before changing map dimensions.");
        }
    }

    public MvEventView? GetEvent(int id)
    {
        JsonPath path = Path.Append("events").Append(id);
        JsonSyntaxNode? node = Document.Syntax.Resolve(path);
        return node is null || node.Kind == JsonSyntaxKind.Null ? null : new MvEventView(Document, path);
    }
}

public sealed class MvMapDataBuffer
{
    private readonly MutableJsonProjectDocument _document;
    private readonly int[] _values;
    private readonly SortedSet<int> _dirtyIndexes = [];

    private MvMapDataBuffer(MutableJsonProjectDocument document, int width, int height, int[] values)
    {
        _document = document;
        Width = width;
        Height = height;
        _values = values;
        if (width <= 0 || height <= 0)
        {
            LayerCount = 0;
        }
        else
        {
            int cellsPerLayer = checked(width * height);
            if (values.Length % cellsPerLayer != 0)
            {
                throw new InvalidDataException(
                    $"Map data length {values.Length} is not divisible by width × height ({cellsPerLayer}).");
            }

            LayerCount = values.Length / cellsPerLayer;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int LayerCount { get; }
    public int Count => _values.Length;
    public bool IsDirty => _dirtyIndexes.Count > 0;
    public IReadOnlyCollection<int> DirtyIndexes => _dirtyIndexes;

    public static MvMapDataBuffer Load(MutableJsonProjectDocument document, int width, int height)
    {
        JsonSyntaxNode? node = document.Syntax.Resolve("/data");
        if (node is not JsonArraySyntax array) throw new InvalidOperationException("Map data is not a JSON array.");
        int[] values = new int[array.Elements.Count];
        for (int index = 0; index < array.Elements.Count; index++)
        {
            if (array.Elements[index] is not JsonNumberSyntax number
                || !int.TryParse(number.RawText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out values[index]))
            {
                throw new FormatException($"Map data element {index} is not an integer.");
            }
        }

        return new MvMapDataBuffer(document, width, height, values);
    }

    public int GetTile(int x, int y, int layer) => _values[GetIndex(x, y, layer)];

    public void SetTile(int x, int y, int layer, int tileId)
    {
        int index = GetIndex(x, y, layer);
        if (_values[index] == tileId) return;
        _values[index] = tileId;
        _dirtyIndexes.Add(index);
    }

    public void Commit()
    {
        if (!IsDirty) return;
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartArray();
            foreach (int value in _values) writer.WriteNumberValue(value);
            writer.WriteEndArray();
        }

        _document.SetRawJson("/data", stream.ToArray());
        _dirtyIndexes.Clear();
    }

    private int GetIndex(int x, int y, int layer)
    {
        if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
        if (layer < 0 || layer >= LayerCount) throw new ArgumentOutOfRangeException(nameof(layer));
        return checked((layer * Height + y) * Width + x);
    }
}
