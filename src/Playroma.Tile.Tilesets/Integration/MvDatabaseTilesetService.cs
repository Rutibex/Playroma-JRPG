using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.RpgMakerMV.Formats;
using Playroma.Tile.RpgMakerMV.Imaging;
using Playroma.Tile.Tilesets.Services;

namespace Playroma.Tile.Tilesets.Integration;

/// <summary>
/// Bidirectional bridge between an editable Playroma Tile project and the authoritative
/// RPG Maker MV data/Tilesets.json plus img/tilesets database owned by Playroma jRPG.
/// </summary>
public sealed class MvDatabaseTilesetService
{
    private readonly MvProjectIntegrationService _projectIntegration = new();

    public MvProjectInspection Inspect(string mvProjectDirectory)
        => _projectIntegration.Inspect(mvProjectDirectory);

    public MvDatabaseImportResult ImportRecords(
        TileProject project,
        string mvProjectDirectory,
        IEnumerable<int> recordIds,
        bool replaceLinkedTilesets = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(recordIds);
        if (string.IsNullOrWhiteSpace(project.ProjectDirectory))
            throw new InvalidOperationException("The Playroma Tile authoring project must be saved before database tilesets can be loaded.");

        var inspection = Inspect(mvProjectDirectory);
        var database = JsonNode.Parse(File.ReadAllText(inspection.TilesetsJsonPath)) as JsonArray
                       ?? throw new InvalidDataException("Tilesets.json is not a JSON array.");
        var normalizedRoot = NormalizePath(mvProjectDirectory);
        var result = new MvDatabaseImportResult();

        foreach (var recordId in recordIds.Distinct().OrderBy(x => x))
        {
            if (recordId <= 0 || recordId >= database.Count || database[recordId] is not JsonObject record)
            {
                result.Warnings.Add($"Tileset record {recordId} does not exist and was skipped.");
                continue;
            }

            try
            {
                var tileSet = ImportRecord(project, normalizedRoot, recordId, record, replaceLinkedTilesets, result.Warnings);
                result.ImportedTileSets.Add(tileSet);
            }
            catch (Exception exception)
            {
                result.Warnings.Add($"Tileset record {recordId}: {exception.Message}");
            }
        }

        project.ModifiedUtc = DateTimeOffset.UtcNow;
        return result;
    }

    public MvDatabaseExportResult ExportTileSet(
        TileProject project,
        TileSetDefinition tileSet,
        string mvProjectDirectory,
        int? recordId,
        bool createNewRecord,
        bool overwriteImages)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tileSet);

        var definition = TilesetSerializer.LoadOrCreate(project, tileSet);
        var targetId = createNewRecord ? null : recordId ?? tileSet.MvDatabaseRecordId;
        if (!createNewRecord && targetId is null)
            throw new InvalidOperationException("Choose an existing database record or create a new record.");

        var patched = _projectIntegration.Patch(
            project,
            tileSet,
            definition,
            mvProjectDirectory,
            targetId,
            createNewRecord,
            overwriteImages);

        tileSet.MvDatabaseRecordId = patched.RecordId;
        tileSet.MvDatabaseProjectPath = NormalizePath(mvProjectDirectory);
        project.ModifiedUtc = DateTimeOffset.UtcNow;
        return new MvDatabaseExportResult
        {
            TileSet = tileSet,
            RecordId = patched.RecordId,
            BackupDirectory = patched.BackupDirectory,
            UpdatedTilesetsJson = patched.UpdatedTilesetsJson,
            CopiedImages = patched.CopiedImages
        };
    }

    private void ImportGameplayMetadata(TileProject project, TileSetDefinition tileSet, string mvRoot, int recordId)
    {
        var definition = TilesetSerializer.LoadOrCreate(project, tileSet);
        TilesetSerializer.InitializeBindings(tileSet, definition);
        _projectIntegration.ImportMetadata(definition, mvRoot, recordId);
        var reference = TilesetSerializer.GetReference(project, definition);
        TilesetSerializer.Save(project, reference, definition);
    }

    private TileSetDefinition ImportRecord(
        TileProject project,
        string mvRoot,
        int recordId,
        JsonObject record,
        bool replaceLinkedTilesets,
        ICollection<string> warnings)
    {
        var recordName = record["name"]?.GetValue<string>() ?? $"Tileset {recordId}";
        var names = record["tilesetNames"] is JsonArray array
            ? array.Select(x => x?.GetValue<string>() ?? string.Empty).ToArray()
            : Array.Empty<string>();

        var tileSet = project.TileSets.FirstOrDefault(x =>
            x.MvDatabaseRecordId == recordId &&
            PathsEqual(x.MvDatabaseProjectPath, mvRoot));

        if (tileSet is not null && !replaceLinkedTilesets)
        {
            warnings.Add($"{recordId}: {recordName} is already linked and was left unchanged.");
            return tileSet;
        }

        tileSet ??= new TileSetDefinition();
        if (!project.TileSets.Contains(tileSet)) project.TileSets.Add(tileSet);
        tileSet.Name = recordName;
        tileSet.Description = $"Loaded from RPG Maker MV database record {recordId}.";
        tileSet.MvDatabaseRecordId = recordId;
        tileSet.MvDatabaseProjectPath = mvRoot;
        tileSet.Sheets.Clear();

        var recordSheetDirectory = Path.Combine(project.ProjectDirectory, "Sheets", "Database", recordId.ToString("D4"));
        Directory.CreateDirectory(recordSheetDirectory);

        foreach (var slot in Enum.GetValues<MvSheetSlot>())
        {
            var index = (int)slot;
            var databaseName = index < names.Length ? names[index] : string.Empty;
            if (string.IsNullOrWhiteSpace(databaseName)) continue;

            var sourcePath = ResolveTilesheetPath(mvRoot, databaseName);
            if (sourcePath is null)
            {
                warnings.Add($"{recordId}: {recordName} references missing {slot} image '{databaseName}'.");
                continue;
            }

            var originalFileName = Path.GetFileName(sourcePath);
            var destinationFileName = $"{slot}_{ProjectPath.SanitizeFileName(originalFileName)}";
            var destinationPath = Path.Combine(recordSheetDirectory, destinationFileName);
            File.Copy(sourcePath, destinationPath, overwrite: true);

            var logicalRegions = MvLayoutCatalog.GetLogicalRegions(slot);
            var size = BitmapFile.ReadSize(sourcePath);
            tileSet.Sheets.Add(new TileSheetDefinition
            {
                Slot = slot,
                DisplayName = Path.GetFileNameWithoutExtension(originalFileName),
                // Keep the database filename for round-trip export while the private authoring copy
                // uses a slot-prefixed name to avoid collisions.
                FileName = originalFileName,
                RelativeImagePath = Path.GetRelativePath(project.ProjectDirectory, destinationPath),
                SourceFileName = originalFileName,
                SourceSha256 = BitmapFile.ComputeSha256(sourcePath),
                PixelWidth = size.Width,
                PixelHeight = size.Height,
                Regions = logicalRegions.Select((region, regionIndex) => new TileRegionMetadata
                {
                    Index = regionIndex,
                    Name = region.DefaultName
                }).ToList()
            });
        }

        ImportGameplayMetadata(project, tileSet, mvRoot, recordId);
        return tileSet;
    }

    private static string? ResolveTilesheetPath(string mvRoot, string databaseName)
    {
        var imageDirectory = Path.Combine(mvRoot, "img", "tilesets");
        var safeName = Path.GetFileName(databaseName);
        var candidates = new List<string>();
        if (Path.HasExtension(safeName)) candidates.Add(Path.Combine(imageDirectory, safeName));
        else
        {
            candidates.Add(Path.Combine(imageDirectory, safeName + ".png"));
            candidates.Add(Path.Combine(imageDirectory, safeName));
        }

        var direct = candidates.FirstOrDefault(File.Exists);
        if (direct is not null) return direct;
        if (!Directory.Exists(imageDirectory)) return null;
        return Directory.EnumerateFiles(imageDirectory, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(
                Path.GetFileNameWithoutExtension(path),
                Path.GetFileNameWithoutExtension(safeName),
                StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class MvDatabaseImportResult
{
    public List<TileSetDefinition> ImportedTileSets { get; } = new();
    public List<string> Warnings { get; } = new();
}

public sealed class MvDatabaseExportResult
{
    public required TileSetDefinition TileSet { get; init; }
    public int RecordId { get; init; }
    public string BackupDirectory { get; init; } = string.Empty;
    public string UpdatedTilesetsJson { get; init; } = string.Empty;
    public List<string> CopiedImages { get; init; } = new();
}
