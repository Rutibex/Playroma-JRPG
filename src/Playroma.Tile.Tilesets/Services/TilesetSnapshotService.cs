using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public sealed class TilesetSnapshotService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TilesetSnapshotRecord CreateSnapshot(TileProject project, TileSetDefinition tileSet,
        TilesetAssemblyDefinition definition, string name, string reason)
    {
        var record = new TilesetSnapshotRecord
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Snapshot {DateTime.Now:yyyy-MM-dd HH-mm}" : name.Trim(),
            Reason = reason
        };
        var relative = ProjectPath.ToPortableRelativePath(Path.Combine("Snapshots", definition.Id.ToString("N"), record.Id.ToString("N")));
        var directory = ProjectPath.Resolve(project, relative);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "tileset.tileset.json"), JsonSerializer.Serialize(definition, Options));
        foreach (var sheet in tileSet.Sheets)
        {
            var source = ProjectPath.Resolve(project, sheet.RelativeImagePath);
            if (File.Exists(source)) File.Copy(source, Path.Combine(directory, sheet.FileName), true);
        }
        record.RelativeDirectory = relative;
        definition.Snapshots.Add(record);
        definition.Revision++;
        return record;
    }

    public TilesetAssemblyDefinition RestoreSnapshot(TileProject project, TileSetDefinition tileSet,
        TilesetSnapshotRecord record, bool restoreImages)
    {
        var directory = ProjectPath.Resolve(project, record.RelativeDirectory);
        var definitionPath = Path.Combine(directory, "tileset.tileset.json");
        var restored = JsonSerializer.Deserialize<TilesetAssemblyDefinition>(File.ReadAllText(definitionPath), Options)
                       ?? throw new InvalidDataException("The snapshot definition is invalid.");
        if (restoreImages)
        {
            foreach (var sheet in tileSet.Sheets)
            {
                var source = Path.Combine(directory, sheet.FileName);
                if (File.Exists(source)) File.Copy(source, ProjectPath.Resolve(project, sheet.RelativeImagePath), true);
            }
        }
        restored.Revision++;
        restored.ModifiedUtc = DateTimeOffset.UtcNow;
        return restored;
    }
}
