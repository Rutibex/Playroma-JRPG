using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Integration;

public sealed class MvProjectIntegrationService
{
    public MvProjectInspection Inspect(string projectDirectory)
    {
        var root = Path.GetFullPath(projectDirectory);
        var dataPath = Path.Combine(root, "data", "Tilesets.json");
        if (!File.Exists(dataPath)) throw new FileNotFoundException("RPG Maker MV data/Tilesets.json was not found.", dataPath);
        var rootNode = JsonNode.Parse(File.ReadAllText(dataPath)) as JsonArray
                       ?? throw new InvalidDataException("Tilesets.json is not a JSON array.");
        var result = new MvProjectInspection { ProjectDirectory = root, TilesetsJsonPath = dataPath };
        for (var i = 1; i < rootNode.Count; i++)
        {
            if (rootNode[i] is not JsonObject item) continue;
            result.Records.Add(new MvTilesetRecordSummary
            {
                Id = item["id"]?.GetValue<int>() ?? i,
                Name = item["name"]?.GetValue<string>() ?? $"Tileset {i}",
                Mode = item["mode"]?.GetValue<int>() ?? 1,
                TilesetNames = item["tilesetNames"] is JsonArray names
                    ? names.Select(x => x?.GetValue<string>() ?? string.Empty).ToArray()
                    : Array.Empty<string>()
            });
        }
        return result;
    }

    public MvProjectPatchResult Patch(TileProject project, TileSetDefinition tileSet,
        TilesetAssemblyDefinition definition, string mvProjectDirectory, int? recordId,
        bool createNewRecord, bool overwriteImages)
    {
        var inspection = Inspect(mvProjectDirectory);
        var rootNode = JsonNode.Parse(File.ReadAllText(inspection.TilesetsJsonPath)) as JsonArray
                       ?? throw new InvalidDataException("Tilesets.json is not a JSON array.");
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupDirectory = Path.Combine(mvProjectDirectory, "PlayromaBackups", $"{timestamp}_{Sanitize(tileSet.Name)}");
        Directory.CreateDirectory(backupDirectory);
        File.Copy(inspection.TilesetsJsonPath, Path.Combine(backupDirectory, "Tilesets.json"), true);

        var imageDirectory = Path.Combine(mvProjectDirectory, "img", "tilesets");
        Directory.CreateDirectory(imageDirectory);
        var names = new string[9];
        foreach (var sheet in tileSet.Sheets.OrderBy(x => x.Slot))
        {
            var source = ProjectPath.Resolve(project, sheet.RelativeImagePath);
            var baseName = Path.GetFileNameWithoutExtension(sheet.FileName);
            names[(int)sheet.Slot] = baseName;
            var destination = Path.Combine(imageDirectory, sheet.FileName);
            if (File.Exists(destination)) File.Copy(destination, Path.Combine(backupDirectory, sheet.FileName), true);
            if (File.Exists(destination) && !overwriteImages) throw new IOException($"Tileset image already exists: {destination}");
            File.Copy(source, destination, true);
        }

        JsonObject record;
        int targetId;
        if (createNewRecord)
        {
            targetId = FindNextId(rootNode);
            while (rootNode.Count <= targetId) rootNode.Add(null);
            record = CreateRecord(targetId, tileSet.Name);
            rootNode[targetId] = record;
        }
        else
        {
            targetId = recordId ?? throw new ArgumentException("An existing tileset record ID is required.", nameof(recordId));
            record = rootNode[targetId] as JsonObject
                     ?? throw new InvalidDataException($"Tileset record {targetId} does not exist.");
        }

        var flags = record["flags"] is JsonArray existingFlags
            ? existingFlags.Select(x => x?.GetValue<int>() ?? 0).ToList()
            : new List<int>();
        while (flags.Count < 8192) flags.Add(0);

        // Clear the complete address space represented by the exported sheets first. This is
        // essential when a tile was changed from blocked/ladder/etc. back to ordinary passable:
        // an absent metadata row must remove the old MV flag instead of silently preserving it.
        foreach (var sheet in tileSet.Sheets.Select(x => x.Slot).Distinct())
        {
            var logicalCount = Playroma.Tile.RpgMakerMV.Formats.MvLayoutCatalog.GetLogicalRegions(sheet).Count;
            for (var logicalIndex = 0; logicalIndex < logicalCount; logicalIndex++)
            {
                foreach (var tileId in MvTileFlagCodec.GetMvTileIds(sheet, logicalIndex))
                {
                    if (tileId >= 0 && tileId < flags.Count) flags[tileId] = 0;
                }
            }
        }

        foreach (var cell in definition.Metadata)
        {
            var encoded = MvTileFlagCodec.Encode(cell.Metadata);
            foreach (var tileId in MvTileFlagCodec.GetMvTileIds(cell.Sheet, cell.CellIndex))
                if (tileId >= 0 && tileId < flags.Count) flags[tileId] = encoded;
        }

        record["id"] = targetId;
        record["name"] = tileSet.Name;
        record["tilesetNames"] = ToJsonArray(names);
        record["flags"] = ToJsonArray(flags);
        if (record["mode"] is null) record["mode"] = 1;
        if (record["note"] is null) record["note"] = string.Empty;

        var temp = inspection.TilesetsJsonPath + ".playroma.tmp";
        File.WriteAllText(temp, rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        File.Move(temp, inspection.TilesetsJsonPath, true);
        return new MvProjectPatchResult
        {
            RecordId = targetId,
            BackupDirectory = backupDirectory,
            UpdatedTilesetsJson = inspection.TilesetsJsonPath,
            CopiedImages = tileSet.Sheets.Select(x => Path.Combine(imageDirectory, x.FileName)).ToList()
        };
    }

    public void ImportMetadata(TilesetAssemblyDefinition definition, string projectDirectory, int recordId)
    {
        var inspection = Inspect(projectDirectory);
        var rootNode = JsonNode.Parse(File.ReadAllText(inspection.TilesetsJsonPath)) as JsonArray
                       ?? throw new InvalidDataException("Tilesets.json is not a JSON array.");
        var record = rootNode[recordId] as JsonObject
                     ?? throw new InvalidDataException($"Tileset record {recordId} does not exist.");
        var flags = record["flags"] as JsonArray ?? new JsonArray();
        definition.Metadata.Clear();
        foreach (var sheet in Enum.GetValues<MvSheetSlot>())
        {
            var count = Playroma.Tile.RpgMakerMV.Formats.MvLayoutCatalog.GetLogicalRegions(sheet).Count;
            for (var index = 0; index < count; index++)
            {
                var tileId = MvTileFlagCodec.GetMvTileIds(sheet, index).First();
                var value = tileId < flags.Count ? flags[tileId]?.GetValue<int>() ?? 0 : 0;
                if (value == 0) continue;
                definition.Metadata.Add(new TileCellMetadata
                {
                    Sheet = sheet,
                    CellIndex = index,
                    Metadata = MvTileFlagCodec.Decode(value)
                });
            }
        }
        definition.Revision++;
    }

    private static JsonObject CreateRecord(int id, string name) => new()
    {
        ["id"] = id,
        ["flags"] = ToJsonArray(Enumerable.Repeat(0, 8192)),
        ["mode"] = 1,
        ["name"] = name,
        ["note"] = string.Empty,
        ["tilesetNames"] = ToJsonArray(Enumerable.Repeat(string.Empty, 9))
    };

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values) result.Add(value);
        return result;
    }

    private static JsonArray ToJsonArray(IEnumerable<int> values)
    {
        var result = new JsonArray();
        foreach (var value in values) result.Add(value);
        return result;
    }

    private static int FindNextId(JsonArray array)
    {
        for (var i = 1; i < array.Count; i++) if (array[i] is null) return i;
        return array.Count;
    }

    private static string Sanitize(string value)
        => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

public sealed class MvProjectInspection
{
    public string ProjectDirectory { get; set; } = string.Empty;
    public string TilesetsJsonPath { get; set; } = string.Empty;
    public List<MvTilesetRecordSummary> Records { get; set; } = new();
}

public sealed class MvTilesetRecordSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Mode { get; set; }
    public IReadOnlyList<string> TilesetNames { get; set; } = Array.Empty<string>();
    public override string ToString() => $"{Id}: {Name}";
}

public sealed class MvProjectPatchResult
{
    public int RecordId { get; set; }
    public string BackupDirectory { get; set; } = string.Empty;
    public string UpdatedTilesetsJson { get; set; } = string.Empty;
    public List<string> CopiedImages { get; set; } = new();
}
