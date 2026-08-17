using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

List<(string Name, Action Test)> tests =
[
    ("Flag edits preserve vehicle and unknown bits", FlagMaskPreservation),
    ("Passage edits affect only passage bits", PassageMaskPreservation),
    ("Terrain edits affect only terrain bits", TerrainMaskPreservation),
    ("Logical autotiles expand to forty-eight shapes", AutotileExpansion),
    ("Wall-top passage keeps intrinsic shape edges", WallTopPassageRules),
    ("Tileset paint stroke is one undoable transaction", TilesetUndoTransaction),
    ("Normal tiles do not expand", NormalTileAddressing),
    ("New tileset templates contain 8192 flags", TilesetTemplate),
    ("Tileset reader preserves extended arrays", TilesetReader),
    ("System reader loads Types, Terms, options, and extension data", SystemReader),
    ("Audio serializer preserves unknown fields", AudioExtensionPreservation),
    ("Vehicle serializer preserves unknown fields", VehicleExtensionPreservation),
    ("Attack motion serializer preserves unknown fields", AttackMotionExtensionPreservation),
    ("Battle-test serializer preserves unknown fields", BattleTestExtensionPreservation),
    ("Terms placeholder validator detects dropped placeholders", PlaceholderValidation),
    ("Escaped percent markers are tracked", EscapedPercentValidation),
    ("System references include magic types and battle-test troop", SystemReferenceIndexing)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 9E tests passed.");
return failures == 0 ? 0 : 1;

static void FlagMaskPreservation()
{
    const int pluginBit = 0x0010_0000;
    int original = MvTilesetFlag.BoatBlockedMask | MvTilesetFlag.ShipBlockedMask | pluginBit;
    int changed = new TilesetFlagEdit(TilesetFlagProperty.Ladder, 1).Apply(original);
    True((changed & MvTilesetFlag.LadderMask) != 0);
    Equal(original & (MvTilesetFlag.BoatBlockedMask | MvTilesetFlag.ShipBlockedMask | pluginBit), changed & (MvTilesetFlag.BoatBlockedMask | MvTilesetFlag.ShipBlockedMask | pluginBit));
}

static void PassageMaskPreservation()
{
    int original = MvTilesetFlag.StarMask | MvTilesetFlag.CounterMask | MvTilesetFlag.AirshipLandingBlockedMask | 0x0040_0000;
    int changed = new TilesetFlagEdit(TilesetFlagProperty.Passage, (int)(MvPassageDirection.Down | MvPassageDirection.Up)).Apply(original);
    Equal((int)(MvPassageDirection.Down | MvPassageDirection.Up), changed & MvTilesetFlag.PassageMask);
    Equal(original, changed & ~MvTilesetFlag.PassageMask);
}

static void TerrainMaskPreservation()
{
    int original = MvTilesetFlag.BushMask | MvTilesetFlag.DamageFloorMask | 0x0020_0000;
    int changed = new TilesetFlagEdit(TilesetFlagProperty.TerrainTag, 13).Apply(original);
    Equal(13, new MvTilesetFlag(changed).TerrainTag);
    Equal(original, changed & ~MvTilesetFlag.TerrainTagMask);
}

static void AutotileExpansion()
{
    IReadOnlyList<int> values = MvTilesetFlagAddressing.ExpandLogicalTile(2053, true);
    Equal(48, values.Count);
    Equal(2048, values[0]);
    Equal(2095, values[^1]);
}

static void WallTopPassageRules()
{
    int first = MvAutotileFlagRules.TileIdA4;
    Equal(MvPassageDirection.None, MvAutotileFlagRules.GetIntrinsicPassage(first));
    Equal(MvPassageDirection.Left, MvAutotileFlagRules.GetIntrinsicPassage(first + 16));
    Equal(MvPassageDirection.All, MvAutotileFlagRules.GetIntrinsicPassage(first + 47));

    TilesetFlagEdit passable = new(TilesetFlagProperty.Passage, 0);
    Equal((int)MvPassageDirection.Left, MvAutotileFlagRules.ApplyLogicalEdit(first + 16, 0, passable, true) & MvTilesetFlag.PassageMask);
    Equal(0, MvAutotileFlagRules.ApplyLogicalEdit(first + 16, 0, passable, false) & MvTilesetFlag.PassageMask);

    TilesetFlagEdit blockDown = new(TilesetFlagProperty.Passage, (int)MvPassageDirection.Down);
    Equal((int)(MvPassageDirection.Down | MvPassageDirection.Left), MvAutotileFlagRules.ApplyLogicalEdit(first + 16, 0, blockDown, true) & MvTilesetFlag.PassageMask);
}

static void NormalTileAddressing()
{
    IReadOnlyList<int> values = MvTilesetFlagAddressing.ExpandLogicalTile(12, true);
    Equal(1, values.Count);
    Equal(12, values[0]);
}

static void TilesetUndoTransaction()
{
    string root = FindRepositoryRoot();
    string source = Path.Combine(root, "testdata", "fixtures", "database-catalog-project");
    string temporary = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Phase9E-Undo-" + Guid.NewGuid().ToString("N"));
    CopyDirectory(source, temporary);
    try
    {
        OperationResult<MvEditingSession> opened = MvEditingSession.OpenAsync(temporary).AsTask().GetAwaiter().GetResult();
        MvEditingSession session = opened.Value ?? throw new InvalidOperationException(string.Join("; ", opened.Diagnostics.Select(item => item.Message)));
        try
        {
            MvDatabaseFileSummary summary = session.Workspace.Snapshot.Database.Find(MvDocumentKind.Tilesets) ?? throw new InvalidOperationException("Tilesets fixture missing.");
            MutableJsonProjectDocument document = session.Workspace.OpenDocumentAsync(summary.RelativePath).AsTask().GetAwaiter().GetResult() as MutableJsonProjectDocument
                ?? throw new InvalidOperationException("Tilesets fixture did not open as mutable JSON.");
            MvTilesetSnapshot before = MvPhase9EReader.ReadTileset(document, 1);
            Phase9EMutationService mutations = new(session);
            mutations.PaintTilesetFlagsAsync(summary.RelativePath, 1, before.Flags, [0, 1, 2], new TilesetFlagEdit(TilesetFlagProperty.Ladder, 1), includeAllAutotileShapes: false).AsTask().GetAwaiter().GetResult();
            Equal(1, session.Undo.UndoCount);
            MvTilesetSnapshot edited = MvPhase9EReader.ReadTileset(document, 1);
            True(edited.GetFlag(0).Ladder && edited.GetFlag(1).Ladder && edited.GetFlag(2).Ladder);
            session.Undo.UndoAsync().AsTask().GetAwaiter().GetResult();
            MvTilesetSnapshot restored = MvPhase9EReader.ReadTileset(document, 1);
            Equal(before.Flags[0], restored.Flags[0]);
            Equal(before.Flags[1], restored.Flags[1]);
            Equal(before.Flags[2], restored.Flags[2]);
        }
        finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    finally { try { Directory.Delete(temporary, recursive: true); } catch { } }
}

static void TilesetTemplate()
{
    using JsonDocument json = JsonDocument.Parse(DatabaseRecordTemplateFactory.Create(MvDocumentKind.Tilesets, 1));
    Equal(8192, json.RootElement.GetProperty("flags").GetArrayLength());
    Equal(9, json.RootElement.GetProperty("tilesetNames").GetArrayLength());
}

static void TilesetReader()
{
    int[] flags = Enumerable.Range(0, 8200).ToArray();
    string json = "[null,{\"id\":1,\"name\":\"Extended\",\"mode\":0,\"note\":\"n\",\"tilesetNames\":[\"A1\",\"A2\",\"A3\",\"A4\",\"A5\",\"B\",\"C\",\"D\",\"E\"],\"flags\":" + JsonSerializer.Serialize(flags) + ",\"pluginData\":true}]";
    MvTilesetSnapshot snapshot = MvPhase9EReader.ReadTileset(Create(json, MvDocumentKind.Tilesets, "data/Tilesets.json"), 1);
    Equal(MvTilesetMode.World, snapshot.Mode);
    Equal(8200, snapshot.Flags.Count);
    True(snapshot.RawJson.Contains("pluginData", StringComparison.Ordinal));
}

static void SystemReader()
{
    string json = "{\"gameTitle\":\"Test\",\"currencyUnit\":\"G\",\"locale\":\"en_US\",\"partyMembers\":[1],\"startMapId\":1,\"startX\":2,\"startY\":3,\"boat\":{},\"ship\":{},\"airship\":{},\"sounds\":[],\"menuCommands\":[true,false,true],\"optSideView\":true,\"title1Name\":\"Title\",\"attackMotions\":[{\"type\":1,\"weaponImageId\":2,\"extra\":7}],\"magicSkills\":[1],\"testTroopId\":1,\"testBattlers\":[],\"elements\":[null,\"Normal\"],\"skillTypes\":[null,\"Magic\"],\"weaponTypes\":[null,\"Sword\"],\"armorTypes\":[null,\"General\"],\"equipTypes\":[null,\"Weapon\"],\"switches\":[null,\"Door\"],\"variables\":[null,\"Count\"],\"windowTone\":[0,0,0,0],\"terms\":{\"basic\":[\"Level\"],\"params\":[\"HP\"],\"commands\":[null,\"Fight\"],\"messages\":{\"victory\":\"%1 won!\"}},\"pluginSystemData\":{\"keep\":true}}";
    MvSystemSnapshot snapshot = MvPhase9EReader.ReadSystem(Create(json, MvDocumentKind.System, "data/System.json"));
    Equal("Test", snapshot.GameTitle);
    True(snapshot.Options["optSideView"]);
    Equal("Door", snapshot.Switches[1]);
    True(snapshot.Terms.Commands[0] is null);
    Equal("%1 won!", snapshot.Terms.Messages["victory"]);
    True(snapshot.RawJson.Contains("pluginSystemData", StringComparison.Ordinal));
}

static void AudioExtensionPreservation()
{
    string raw = Phase9EMutationService.SerializeAudio(new MvAudioSettingSnapshot("Bell", 80, 110, -10, "{\"name\":\"Old\",\"volume\":90,\"pitch\":100,\"pan\":0,\"loopStart\":12}"));
    using JsonDocument json = JsonDocument.Parse(raw);
    Equal(12, json.RootElement.GetProperty("loopStart").GetInt32());
    Equal("Bell", json.RootElement.GetProperty("name").GetString());
}

static void VehicleExtensionPreservation()
{
    MvVehicleSnapshot value = new("Vehicle", 3, 4, 5, 6, MvAudioSettingSnapshot.Empty, "{\"pluginMovement\":\"hover\"}");
    using JsonDocument json = JsonDocument.Parse(Phase9EMutationService.SerializeVehicle(value));
    Equal("hover", json.RootElement.GetProperty("pluginMovement").GetString());
    Equal(4, json.RootElement.GetProperty("startMapId").GetInt32());
}

static void AttackMotionExtensionPreservation()
{
    MvAttackMotionSnapshot item = new(2, 9, "{\"type\":0,\"weaponImageId\":0,\"trail\":\"blue\"}");
    using JsonDocument json = JsonDocument.Parse(Phase9EMutationService.SerializeAttackMotions([item]));
    Equal("blue", json.RootElement[0].GetProperty("trail").GetString());
    Equal(9, json.RootElement[0].GetProperty("weaponImageId").GetInt32());
}

static void BattleTestExtensionPreservation()
{
    MvBattleTestBattlerSnapshot item = new(2, 25, [1, 2], "{\"actorId\":1,\"level\":1,\"equips\":[],\"pluginSeed\":42}");
    using JsonDocument json = JsonDocument.Parse(Phase9EMutationService.SerializeBattleTestBattlers([item]));
    Equal(42, json.RootElement[0].GetProperty("pluginSeed").GetInt32());
    Equal(25, json.RootElement[0].GetProperty("level").GetInt32());
}

static void PlaceholderValidation()
{
    IReadOnlyList<string> missing = MvTermPlaceholderValidator.Missing("%1 found %2 and %1.", "%1 found an item.");
    Equal(2, missing.Count);
    True(missing.Contains("%1"));
    True(missing.Contains("%2"));
}

static void EscapedPercentValidation()
{
    IReadOnlyList<string> placeholders = MvTermPlaceholderValidator.Extract("Progress 50%%; actor %1");
    True(placeholders.Contains("%%"));
    True(placeholders.Contains("%1"));
}


static void SystemReferenceIndexing()
{
    string root = FindRepositoryRoot();
    string fixture = Path.Combine(root, "testdata", "fixtures", "database-catalog-project");
    OperationResult<MvProjectWorkspace> opened = MvProjectWorkspace.OpenAsync(fixture).AsTask().GetAwaiter().GetResult();
    MvProjectWorkspace workspace = opened.Value ?? throw new InvalidOperationException(string.Join("; ", opened.Diagnostics.Select(item => item.Message)));
    try
    {
        ProjectReferenceIndex index = new MvProjectReferenceIndexer().BuildAsync(workspace).AsTask().GetAwaiter().GetResult();
        True(index.FindInbound(new ReferenceTarget(ReferenceTargetKind.SkillType, 1), includeHeuristic: false)
            .Any(reference => reference.Source.JsonPath == "/magicSkills/0"), "magicSkills was not indexed as an exact SkillType reference.");
        True(index.FindInbound(new ReferenceTarget(ReferenceTargetKind.Troop, 1), includeHeuristic: false)
            .Any(reference => reference.Source.JsonPath == "/testTroopId"), "testTroopId was not indexed as an exact Troop reference.");
    }
    finally { workspace.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
}

static string FindRepositoryRoot()
{
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "Playroma.JRPG.sln"))) return current.FullName;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate Playroma.JRPG.sln.");
}

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
    }
}

static MutableJsonProjectDocument Create(string json, MvDocumentKind kind, string path)
{
    byte[] bytes = Encoding.UTF8.GetBytes(json);
    ContentFingerprint fingerprint = new(bytes.LongLength, DateTimeOffset.UnixEpoch, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    return MutableJsonProjectDocument.Create(path, kind, bytes, fingerprint);
}

static void True(bool value, string message = "Assertion was false.") { if (!value) throw new InvalidOperationException(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}."); }
