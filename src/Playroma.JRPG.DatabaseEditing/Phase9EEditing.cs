using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;

namespace Playroma.JRPG.DatabaseEditing;

public static class MvPhase9EConstants
{
    public const int TilesetNameCount = 9;
    public const int TileFlagCount = 8192;
    public const int AutotileShapeCount = 48;
    public const int RuntimeSoundCount = 24;
    public const int MenuCommandCount = 6;
}

public enum MvTilesetMode
{
    World = 0,
    Area = 1
}

[Flags]
public enum MvPassageDirection
{
    None = 0,
    Down = 0x01,
    Left = 0x02,
    Right = 0x04,
    Up = 0x08,
    All = Down | Left | Right | Up
}

/// <summary>
/// Raw-preserving view over an RPG Maker MV tileset flag integer. Mutators replace only
/// the requested mask so vehicle and plugin-defined bits are never accidentally cleared.
/// </summary>
public readonly record struct MvTilesetFlag(int RawValue)
{
    public const int PassageMask = 0x000f;
    public const int StarMask = 0x0010;
    public const int LadderMask = 0x0020;
    public const int BushMask = 0x0040;
    public const int CounterMask = 0x0080;
    public const int DamageFloorMask = 0x0100;
    public const int BoatBlockedMask = 0x0200;
    public const int ShipBlockedMask = 0x0400;
    public const int AirshipLandingBlockedMask = 0x0800;
    public const int TerrainTagMask = 0xf000;

    public MvPassageDirection BlockedDirections => (MvPassageDirection)(RawValue & PassageMask);
    public bool Star => (RawValue & StarMask) != 0;
    public bool Ladder => (RawValue & LadderMask) != 0;
    public bool Bush => (RawValue & BushMask) != 0;
    public bool Counter => (RawValue & CounterMask) != 0;
    public bool DamageFloor => (RawValue & DamageFloorMask) != 0;
    public bool BoatBlocked => (RawValue & BoatBlockedMask) != 0;
    public bool ShipBlocked => (RawValue & ShipBlockedMask) != 0;
    public bool AirshipLandingBlocked => (RawValue & AirshipLandingBlockedMask) != 0;
    public int TerrainTag => (RawValue >> 12) & 0x0f;
    public int UnknownBits => RawValue & ~(PassageMask | StarMask | LadderMask | BushMask | CounterMask | DamageFloorMask | BoatBlockedMask | ShipBlockedMask | AirshipLandingBlockedMask | TerrainTagMask);

    public MvTilesetFlag WithBlockedDirections(MvPassageDirection value) => WithMask(PassageMask, (int)value & PassageMask);
    public MvTilesetFlag WithStar(bool value) => WithBoolean(StarMask, value);
    public MvTilesetFlag WithLadder(bool value) => WithBoolean(LadderMask, value);
    public MvTilesetFlag WithBush(bool value) => WithBoolean(BushMask, value);
    public MvTilesetFlag WithCounter(bool value) => WithBoolean(CounterMask, value);
    public MvTilesetFlag WithDamageFloor(bool value) => WithBoolean(DamageFloorMask, value);
    public MvTilesetFlag WithBoatBlocked(bool value) => WithBoolean(BoatBlockedMask, value);
    public MvTilesetFlag WithShipBlocked(bool value) => WithBoolean(ShipBlockedMask, value);
    public MvTilesetFlag WithAirshipLandingBlocked(bool value) => WithBoolean(AirshipLandingBlockedMask, value);
    public MvTilesetFlag WithTerrainTag(int value)
    {
        if (value is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(value));
        return WithMask(TerrainTagMask, value << 12);
    }

    public MvTilesetFlag WithMask(int mask, int requestedBits) => new((RawValue & ~mask) | (requestedBits & mask));
    private MvTilesetFlag WithBoolean(int mask, bool value) => WithMask(mask, value ? mask : 0);
}

public sealed record MvTilesetSnapshot(
    int Id,
    string Name,
    MvTilesetMode Mode,
    string Note,
    IReadOnlyList<string> TilesetNames,
    IReadOnlyList<int> Flags,
    string RawJson)
{
    public string GetSheetName(int index) => index >= 0 && index < TilesetNames.Count ? TilesetNames[index] : string.Empty;
    public MvTilesetFlag GetFlag(int tileId) => tileId >= 0 && tileId < Flags.Count ? new MvTilesetFlag(Flags[tileId]) : new MvTilesetFlag(0);
}

public sealed record MvAudioSettingSnapshot(string Name, int Volume, int Pitch, int Pan, string RawJson)
{
    public static MvAudioSettingSnapshot Empty { get; } = new(string.Empty, 90, 100, 0, "{}");
}

public sealed record MvVehicleSnapshot(
    string CharacterName,
    int CharacterIndex,
    int StartMapId,
    int StartX,
    int StartY,
    MvAudioSettingSnapshot Bgm,
    string RawJson);

public sealed record MvAttackMotionSnapshot(int Type, int WeaponImageId, string RawJson);
public sealed record MvBattleTestBattlerSnapshot(int ActorId, int Level, IReadOnlyList<int> Equipment, string RawJson);
public sealed record MvTermsSnapshot(
    IReadOnlyList<string?> Basic,
    IReadOnlyList<string?> Parameters,
    IReadOnlyList<string?> Commands,
    IReadOnlyDictionary<string, string?> Messages,
    string RawJson);

public sealed record MvSystemSnapshot(
    string GameTitle,
    string CurrencyUnit,
    string Locale,
    int EditMapId,
    int VersionId,
    IReadOnlyList<int> PartyMembers,
    int StartMapId,
    int StartX,
    int StartY,
    MvVehicleSnapshot Boat,
    MvVehicleSnapshot Ship,
    MvVehicleSnapshot Airship,
    MvAudioSettingSnapshot TitleBgm,
    MvAudioSettingSnapshot BattleBgm,
    MvAudioSettingSnapshot VictoryMe,
    MvAudioSettingSnapshot DefeatMe,
    MvAudioSettingSnapshot GameoverMe,
    IReadOnlyList<MvAudioSettingSnapshot> Sounds,
    IReadOnlyList<bool> MenuCommands,
    IReadOnlyDictionary<string, bool> Options,
    string Title1Name,
    string Title2Name,
    string Battleback1Name,
    string Battleback2Name,
    string BattlerName,
    int BattlerHue,
    IReadOnlyList<MvAttackMotionSnapshot> AttackMotions,
    IReadOnlyList<int> MagicSkills,
    int TestTroopId,
    IReadOnlyList<MvBattleTestBattlerSnapshot> TestBattlers,
    IReadOnlyList<string?> Elements,
    IReadOnlyList<string?> SkillTypes,
    IReadOnlyList<string?> WeaponTypes,
    IReadOnlyList<string?> ArmorTypes,
    IReadOnlyList<string?> EquipTypes,
    IReadOnlyList<string?> Switches,
    IReadOnlyList<string?> Variables,
    IReadOnlyList<int> WindowTone,
    MvTermsSnapshot Terms,
    string RawJson);

public static class MvPhase9EReader
{
    public static MvTilesetSnapshot ReadTileset(MutableJsonProjectDocument document, int recordId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (recordId <= 0) throw new ArgumentOutOfRangeException(nameof(recordId));
        string raw = document.GetRawJson(JsonPath.Root.Append(recordId))
            ?? throw new KeyNotFoundException($"Tileset {recordId} does not exist.");
        using JsonDocument json = JsonDocument.Parse(raw);
        JsonElement root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"Tileset {recordId} is not an object.");
        return new MvTilesetSnapshot(
            ReadInt(root, "id", recordId),
            ReadString(root, "name"),
            ReadInt(root, "mode", 1) == 0 ? MvTilesetMode.World : MvTilesetMode.Area,
            ReadString(root, "note"),
            ReadStringArray(root, "tilesetNames"),
            ReadIntArray(root, "flags"),
            raw);
    }

    public static MvSystemSnapshot ReadSystem(MutableJsonProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string raw = document.GetRawJson(JsonPath.Root) ?? "{}";
        using JsonDocument json = JsonDocument.Parse(raw);
        JsonElement root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("System.json is not an object.");
        Dictionary<string, bool> options = new(StringComparer.Ordinal)
        {
            ["optSideView"] = ReadBool(root, "optSideView"),
            ["optTransparent"] = ReadBool(root, "optTransparent"),
            ["optFollowers"] = ReadBool(root, "optFollowers"),
            ["optSlipDeath"] = ReadBool(root, "optSlipDeath"),
            ["optFloorDeath"] = ReadBool(root, "optFloorDeath"),
            ["optDisplayTp"] = ReadBool(root, "optDisplayTp"),
            ["optExtraExp"] = ReadBool(root, "optExtraExp"),
            ["optDrawTitle"] = ReadBool(root, "optDrawTitle")
        };
        return new MvSystemSnapshot(
            ReadString(root, "gameTitle"), ReadString(root, "currencyUnit"), ReadString(root, "locale"), ReadInt(root, "editMapId"), ReadInt(root, "versionId"),
            ReadIntArray(root, "partyMembers"), ReadInt(root, "startMapId"), ReadInt(root, "startX"), ReadInt(root, "startY"),
            ReadVehicle(root, "boat"), ReadVehicle(root, "ship"), ReadVehicle(root, "airship"),
            ReadAudio(root, "titleBgm"), ReadAudio(root, "battleBgm"), ReadAudio(root, "victoryMe"), ReadAudio(root, "defeatMe"), ReadAudio(root, "gameoverMe"),
            ReadAudioArray(root, "sounds"), ReadBoolArray(root, "menuCommands"), options,
            ReadString(root, "title1Name"), ReadString(root, "title2Name"), ReadString(root, "battleback1Name"), ReadString(root, "battleback2Name"), ReadString(root, "battlerName"), ReadInt(root, "battlerHue"),
            ReadAttackMotions(root), ReadIntArray(root, "magicSkills"), ReadInt(root, "testTroopId"), ReadBattleTestBattlers(root),
            ReadNullableStringArray(root, "elements"), ReadNullableStringArray(root, "skillTypes"), ReadNullableStringArray(root, "weaponTypes"), ReadNullableStringArray(root, "armorTypes"), ReadNullableStringArray(root, "equipTypes"), ReadNullableStringArray(root, "switches"), ReadNullableStringArray(root, "variables"),
            ReadIntArray(root, "windowTone"), ReadTerms(root), raw);
    }

    private static MvVehicleSnapshot ReadVehicle(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            return new MvVehicleSnapshot(string.Empty, 0, 0, 0, 0, MvAudioSettingSnapshot.Empty, "{}");
        return new MvVehicleSnapshot(ReadString(value, "characterName"), ReadInt(value, "characterIndex"), ReadInt(value, "startMapId"), ReadInt(value, "startX"), ReadInt(value, "startY"), ReadAudio(value, "bgm"), value.GetRawText());
    }

    private static MvAudioSettingSnapshot ReadAudio(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return MvAudioSettingSnapshot.Empty;
        return ReadAudio(value);
    }

    private static MvAudioSettingSnapshot ReadAudio(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return MvAudioSettingSnapshot.Empty with { RawJson = value.GetRawText() };
        return new MvAudioSettingSnapshot(ReadString(value, "name"), ReadInt(value, "volume", 90), ReadInt(value, "pitch", 100), ReadInt(value, "pan"), value.GetRawText());
    }

    private static IReadOnlyList<MvAudioSettingSnapshot> ReadAudioArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray().Select(ReadAudio).ToArray();
    }

    private static IReadOnlyList<MvAttackMotionSnapshot> ReadAttackMotions(JsonElement root)
    {
        if (!root.TryGetProperty("attackMotions", out JsonElement array) || array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray().Select(item => new MvAttackMotionSnapshot(ReadInt(item, "type"), ReadInt(item, "weaponImageId"), item.GetRawText())).ToArray();
    }

    private static IReadOnlyList<MvBattleTestBattlerSnapshot> ReadBattleTestBattlers(JsonElement root)
    {
        if (!root.TryGetProperty("testBattlers", out JsonElement array) || array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray().Select(item => new MvBattleTestBattlerSnapshot(ReadInt(item, "actorId"), ReadInt(item, "level", 1), ReadIntArray(item, "equips"), item.GetRawText())).ToArray();
    }

    private static MvTermsSnapshot ReadTerms(JsonElement root)
    {
        if (!root.TryGetProperty("terms", out JsonElement terms) || terms.ValueKind != JsonValueKind.Object)
            return new MvTermsSnapshot([], [], [], new Dictionary<string, string?>(), "{}");
        Dictionary<string, string?> messages = new(StringComparer.Ordinal);
        if (terms.TryGetProperty("messages", out JsonElement messageObject) && messageObject.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in messageObject.EnumerateObject()) messages[property.Name] = ReadNullableString(property.Value);
        }
        return new MvTermsSnapshot(ReadNullableStringArray(terms, "basic"), ReadNullableStringArray(terms, "params"), ReadNullableStringArray(terms, "commands"), messages, terms.GetRawText());
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string property) => ReadNullableStringArray(root, property).Select(value => value ?? string.Empty).ToArray();
    private static IReadOnlyList<string?> ReadNullableStringArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray().Select(ReadNullableString).ToArray();
    }
    private static string? ReadNullableString(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Null => null, _ => value.GetRawText() };
    private static IReadOnlyList<int> ReadIntArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray().Select(value => value.TryGetInt32(out int parsed) ? parsed : 0).ToArray();
    }
    private static IReadOnlyList<bool> ReadBoolArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.True).ToArray();
    }
    private static string ReadString(JsonElement root, string property) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static int ReadInt(JsonElement root, string property, int fallback = 0) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int parsed) ? parsed : fallback;
    private static bool ReadBool(JsonElement root, string property) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}

public enum TilesetFlagProperty
{
    Passage,
    Star,
    Ladder,
    Bush,
    Counter,
    DamageFloor,
    BoatBlocked,
    ShipBlocked,
    AirshipLandingBlocked,
    TerrainTag,
    RawValue
}

public sealed record TilesetFlagEdit(TilesetFlagProperty Property, int Value)
{
    public int Apply(int rawValue)
    {
        MvTilesetFlag flag = new(rawValue);
        return Property switch
        {
            TilesetFlagProperty.Passage => flag.WithBlockedDirections((MvPassageDirection)(Value & MvTilesetFlag.PassageMask)).RawValue,
            TilesetFlagProperty.Star => flag.WithStar(Value != 0).RawValue,
            TilesetFlagProperty.Ladder => flag.WithLadder(Value != 0).RawValue,
            TilesetFlagProperty.Bush => flag.WithBush(Value != 0).RawValue,
            TilesetFlagProperty.Counter => flag.WithCounter(Value != 0).RawValue,
            TilesetFlagProperty.DamageFloor => flag.WithDamageFloor(Value != 0).RawValue,
            TilesetFlagProperty.BoatBlocked => flag.WithBoatBlocked(Value != 0).RawValue,
            TilesetFlagProperty.ShipBlocked => flag.WithShipBlocked(Value != 0).RawValue,
            TilesetFlagProperty.AirshipLandingBlocked => flag.WithAirshipLandingBlocked(Value != 0).RawValue,
            TilesetFlagProperty.TerrainTag => flag.WithTerrainTag(Value).RawValue,
            TilesetFlagProperty.RawValue => Value,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}

public static class MvTilesetFlagAddressing
{
    public static bool IsAutotile(int tileId) => tileId >= 2048 && tileId < MvPhase9EConstants.TileFlagCount;
    public static IReadOnlyList<int> ExpandLogicalTile(int tileId, bool includeAllAutotileShapes)
    {
        if (tileId < 0 || tileId >= MvPhase9EConstants.TileFlagCount) throw new ArgumentOutOfRangeException(nameof(tileId));
        if (!includeAllAutotileShapes || !IsAutotile(tileId)) return [tileId];
        int first = tileId - (tileId - 2048) % MvPhase9EConstants.AutotileShapeCount;
        return Enumerable.Range(first, MvPhase9EConstants.AutotileShapeCount).ToArray();
    }
}

/// <summary>
/// MV wall-top autotiles carry shape-dependent passage masks even when the logical
/// palette tile is set to passable. This table mirrors the 48 floor-autotile shapes:
/// it prevents movement across an exposed wall-top edge without flattening every
/// stored shape to the same flag value.
/// </summary>
public static class MvAutotileFlagRules
{
    public const int TileIdA4 = 5888;
    public const int TileIdMax = 8192;

    private static readonly MvPassageDirection[] WallTopPassageShapeMasks =
    [
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        MvPassageDirection.Left, MvPassageDirection.Left, MvPassageDirection.Left, MvPassageDirection.Left,
        MvPassageDirection.Up, MvPassageDirection.Up, MvPassageDirection.Up, MvPassageDirection.Up,
        MvPassageDirection.Right, MvPassageDirection.Right, MvPassageDirection.Right, MvPassageDirection.Right,
        0, 0, 0, 0,
        MvPassageDirection.Left | MvPassageDirection.Right,
        MvPassageDirection.Up,
        MvPassageDirection.Left | MvPassageDirection.Up,
        MvPassageDirection.Left | MvPassageDirection.Up,
        MvPassageDirection.Right | MvPassageDirection.Up,
        MvPassageDirection.Right | MvPassageDirection.Up,
        MvPassageDirection.Right,
        MvPassageDirection.Right,
        MvPassageDirection.Left,
        MvPassageDirection.Left,
        MvPassageDirection.Left | MvPassageDirection.Right | MvPassageDirection.Up,
        MvPassageDirection.Left | MvPassageDirection.Up,
        MvPassageDirection.Left | MvPassageDirection.Right,
        MvPassageDirection.Right | MvPassageDirection.Up,
        MvPassageDirection.Left | MvPassageDirection.Right | MvPassageDirection.Up,
        MvPassageDirection.All
    ];

    public static bool IsWallTopAutotile(int tileId)
    {
        if (tileId < TileIdA4 || tileId >= TileIdMax) return false;
        int kind = (tileId - 2048) / MvPhase9EConstants.AutotileShapeCount;
        return kind % 16 < 8;
    }

    public static MvPassageDirection GetIntrinsicPassage(int tileId)
    {
        if (!IsWallTopAutotile(tileId)) return MvPassageDirection.None;
        int shape = (tileId - 2048) % MvPhase9EConstants.AutotileShapeCount;
        return WallTopPassageShapeMasks[shape];
    }

    public static int ApplyLogicalEdit(int tileId, int rawValue, TilesetFlagEdit edit, bool logicalAutotileEdit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!logicalAutotileEdit || edit.Property != TilesetFlagProperty.Passage || !IsWallTopAutotile(tileId))
            return edit.Apply(rawValue);

        MvPassageDirection requested = (MvPassageDirection)(edit.Value & MvTilesetFlag.PassageMask);
        MvPassageDirection combined = requested | GetIntrinsicPassage(tileId);
        return new MvTilesetFlag(rawValue).WithBlockedDirections(combined).RawValue;
    }
}

public sealed class Phase9EMutationService
{
    private readonly MvEditingSession _session;
    public Phase9EMutationService(MvEditingSession session) => _session = session ?? throw new ArgumentNullException(nameof(session));

    public ValueTask SetTilesetIdentityAsync(string relativePath, int recordId, string name, MvTilesetMode mode, string note, CancellationToken cancellationToken = default) =>
        ExecuteAsync($"Change tileset {recordId} settings",
        [
            new SetJsonStringCommand(relativePath, JsonPath.Root.Append(ValidateId(recordId)).Append("name"), name, "Change tileset name"),
            new SetJsonInt32Command(relativePath, JsonPath.Root.Append(recordId).Append("mode"), (int)mode, "Change tileset mode"),
            new SetJsonStringCommand(relativePath, JsonPath.Root.Append(recordId).Append("note"), note, "Change tileset note")
        ], cancellationToken);

    public ValueTask SetTilesetSheetAsync(string relativePath, int recordId, int sheetIndex, string assetName, CancellationToken cancellationToken = default)
    {
        if (sheetIndex is < 0 or >= MvPhase9EConstants.TilesetNameCount) throw new ArgumentOutOfRangeException(nameof(sheetIndex));
        return ExecuteAsync($"Set tileset {recordId} sheet {sheetIndex + 1}", [new SetJsonStringCommand(relativePath, JsonPath.Root.Append(ValidateId(recordId)).Append("tilesetNames").Append(sheetIndex), assetName)], cancellationToken);
    }

    public ValueTask PaintTilesetFlagsAsync(
        string relativePath,
        int recordId,
        IReadOnlyList<int> currentFlags,
        IEnumerable<int> logicalTileIds,
        TilesetFlagEdit edit,
        bool includeAllAutotileShapes = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentFlags);
        ArgumentNullException.ThrowIfNull(logicalTileIds);
        ArgumentNullException.ThrowIfNull(edit);
        HashSet<int> addressed = [];
        foreach (int tileId in logicalTileIds)
            foreach (int expanded in MvTilesetFlagAddressing.ExpandLogicalTile(tileId, includeAllAutotileShapes)) addressed.Add(expanded);
        List<IEditorCommand> commands = [];
        JsonPath flagsPath = JsonPath.Root.Append(ValidateId(recordId)).Append("flags");
        foreach (int tileId in addressed.OrderBy(value => value))
        {
            if (tileId < 0 || tileId >= currentFlags.Count) throw new InvalidOperationException($"Tile flag {tileId} is outside the stored flags array ({currentFlags.Count} entries). Normalize the array explicitly before painting.");
            int before = currentFlags[tileId];
            int after = MvAutotileFlagRules.ApplyLogicalEdit(tileId, before, edit, includeAllAutotileShapes);
            if (before == after) continue;
            commands.Add(new SetJsonInt32Command(relativePath, flagsPath.Append(tileId), after, $"Paint tileset flag {tileId}"));
        }
        return commands.Count == 0 ? ValueTask.CompletedTask : ExecuteAsync($"Paint {commands.Count} tileset flag value(s)", commands, cancellationToken);
    }

    public ValueTask NormalizeTilesetFlagsAsync(string relativePath, int recordId, IReadOnlyList<int> currentFlags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentFlags);
        int[] normalized = new int[MvPhase9EConstants.TileFlagCount];
        for (int index = 0; index < Math.Min(currentFlags.Count, normalized.Length); index++) normalized[index] = currentFlags[index];
        return SetRawAsync(relativePath, JsonPath.Root.Append(ValidateId(recordId)).Append("flags"), JsonSerializer.Serialize(normalized), $"Normalize tileset {recordId} flag array", cancellationToken);
    }

    public ValueTask SetSystemStringAsync(string relativePath, string property, string value, CancellationToken cancellationToken = default) =>
        ExecuteAsync($"Change System {property}", [new SetJsonStringCommand(relativePath, JsonPath.Root.Append(property), value)], cancellationToken);

    public ValueTask SetSystemIntAsync(string relativePath, string property, int value, CancellationToken cancellationToken = default) =>
        ExecuteAsync($"Change System {property}", [new SetJsonInt32Command(relativePath, JsonPath.Root.Append(property), value)], cancellationToken);

    public ValueTask SetSystemBoolAsync(string relativePath, string property, bool value, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, JsonPath.Root.Append(property), value ? "true" : "false", $"Change System {property}", cancellationToken);

    public ValueTask SetSystemArrayAsync<T>(string relativePath, string property, IReadOnlyList<T> values, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, JsonPath.Root.Append(property), JsonSerializer.Serialize(values), $"Change System {property}", cancellationToken);

    public ValueTask SetSystemObjectAsync(string relativePath, string property, string rawJson, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, JsonPath.Root.Append(property), rawJson, $"Change System {property}", cancellationToken);

    public ValueTask SetSystemArrayEntryAsync(string relativePath, string property, int index, string? value, CancellationToken cancellationToken = default)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        string raw = value is null ? "null" : EncodeString(value);
        return SetRawAsync(relativePath, AppendRelativePath(JsonPath.Root, property).Append(index), raw, $"Change {property} #{index}", cancellationToken);
    }

    public ValueTask SetTermMessageAsync(string relativePath, string key, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        string raw = value is null ? "null" : EncodeString(value);
        return SetRawAsync(relativePath, JsonPath.Root.Append("terms").Append("messages").Append(key), raw, $"Change term message {key}", cancellationToken);
    }

    public ValueTask SetAudioAsync(string relativePath, JsonPath path, MvAudioSettingSnapshot value, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, path, SerializeAudio(value), $"Change audio setting {path}", cancellationToken);

    public ValueTask SetVehicleAsync(string relativePath, string property, MvVehicleSnapshot value, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, JsonPath.Root.Append(property), SerializeVehicle(value), $"Change {property}", cancellationToken);

    public ValueTask SetAttackMotionsAsync(string relativePath, IReadOnlyList<MvAttackMotionSnapshot> values, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, JsonPath.Root.Append("attackMotions"), SerializeAttackMotions(values), "Change attack motions", cancellationToken);

    public ValueTask SetBattleTestBattlersAsync(string relativePath, IReadOnlyList<MvBattleTestBattlerSnapshot> values, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, JsonPath.Root.Append("testBattlers"), SerializeBattleTestBattlers(values), "Change battle test actors", cancellationToken);

    public static string SerializeAudio(MvAudioSettingSnapshot value)
    {
        JsonObject item = ParseObject(value.RawJson);
        item["name"] = value.Name;
        item["volume"] = value.Volume;
        item["pitch"] = value.Pitch;
        item["pan"] = value.Pan;
        return item.ToJsonString(Compact);
    }

    public static string SerializeVehicle(MvVehicleSnapshot value)
    {
        JsonObject item = ParseObject(value.RawJson);
        item["characterName"] = value.CharacterName;
        item["characterIndex"] = value.CharacterIndex;
        item["startMapId"] = value.StartMapId;
        item["startX"] = value.StartX;
        item["startY"] = value.StartY;
        item["bgm"] = JsonNode.Parse(SerializeAudio(value.Bgm));
        return item.ToJsonString(Compact);
    }

    public static string SerializeAttackMotions(IReadOnlyList<MvAttackMotionSnapshot> values)
    {
        JsonArray array = [];
        foreach (MvAttackMotionSnapshot value in values)
        {
            JsonObject item = ParseObject(value.RawJson);
            item["type"] = value.Type;
            item["weaponImageId"] = value.WeaponImageId;
            array.Add(item);
        }
        return array.ToJsonString(Compact);
    }

    public static string SerializeBattleTestBattlers(IReadOnlyList<MvBattleTestBattlerSnapshot> values)
    {
        JsonArray array = [];
        foreach (MvBattleTestBattlerSnapshot value in values)
        {
            JsonObject item = ParseObject(value.RawJson);
            item["actorId"] = value.ActorId;
            item["level"] = value.Level;
            item["equips"] = new JsonArray(value.Equipment.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
            array.Add(item);
        }
        return array.ToJsonString(Compact);
    }

    private ValueTask SetRawAsync(string relativePath, JsonPath path, string rawJson, string description, CancellationToken cancellationToken) =>
        ExecuteAsync(description, [new SetJsonValueCommand(relativePath, path, rawJson, description)], cancellationToken);

    private async ValueTask ExecuteAsync(string description, IReadOnlyList<IEditorCommand> commands, CancellationToken cancellationToken)
    {
        await using ProjectTransaction transaction = _session.BeginTransaction(description);
        foreach (IEditorCommand command in commands) transaction.Add(command);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static JsonPath AppendRelativePath(JsonPath root, string relative)
    {
        JsonPath path = root;
        foreach (string segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
            path = int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ? path.Append(index) : path.Append(segment);
        return path;
    }

    private static JsonObject ParseObject(string rawJson) => JsonNode.Parse(string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson) as JsonObject ?? new JsonObject();
    private static string EncodeString(string value) => Encoding.UTF8.GetString(JsonValueEncoder.EncodeString(value ?? string.Empty));
    private static int ValidateId(int id) => id > 0 ? id : throw new ArgumentOutOfRangeException(nameof(id));
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
}

public static class MvTermPlaceholderValidator
{
    public static IReadOnlyList<string> Extract(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        List<string> result = [];
        for (int index = 0; index < text.Length - 1; index++)
        {
            if (text[index] != '%') continue;
            int start = index;
            index++;
            if (text[index] == '%') { result.Add("%%"); continue; }
            int numberStart = index;
            while (index < text.Length && char.IsDigit(text[index])) index++;
            if (index > numberStart) result.Add(text[start..index]);
            index--;
        }
        return result;
    }

    public static IReadOnlyList<string> Missing(string? original, string? edited)
    {
        Dictionary<string, int> expected = Extract(original).GroupBy(value => value, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Dictionary<string, int> actual = Extract(edited).GroupBy(value => value, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return expected.Where(pair => actual.GetValueOrDefault(pair.Key) < pair.Value).Select(pair => pair.Key).ToArray();
    }
}
