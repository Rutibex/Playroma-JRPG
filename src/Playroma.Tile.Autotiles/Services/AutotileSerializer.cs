using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;

namespace Playroma.Tile.Autotiles.Services;

public static class AutotileSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AutotileDefinition Load(TileProject project, AutotileAssetReference reference)
    {
        var path = ProjectPath.Resolve(project, reference.RelativeDefinitionPath);
        var definition = JsonSerializer.Deserialize<AutotileDefinition>(File.ReadAllText(path), Options)
                         ?? throw new InvalidDataException($"Invalid autotile definition: {path}");
        Normalize(definition);
        return definition;
    }

    public static Bitmap LoadSource(TileProject project, AutotileDefinition definition)
    {
        var path = ProjectPath.Resolve(project, definition.SourceImageRelativePath);
        return AutotileBitmap.LoadEditable(path);
    }

    public static AutotileAssetReference SaveNew(TileProject project, AutotileDefinition definition, Bitmap source)
    {
        if (definition.Id == Guid.Empty) definition.Id = Guid.NewGuid();
        var folderRelative = Path.Combine("Autotiles", definition.Id.ToString("N"));
        definition.SourceImageRelativePath = Path.Combine(folderRelative, "source.png");
        var definitionRelative = Path.Combine(folderRelative, "definition.autotile.json");
        Save(project, definition, source, definitionRelative);
        var reference = new AutotileAssetReference
        {
            Id = definition.Id,
            Name = definition.Name,
            RelativeDefinitionPath = definitionRelative,
            Revision = definition.Revision,
            ModifiedUtc = definition.ModifiedUtc
        };
        project.Autotiles.Add(reference);
        return reference;
    }

    public static void Save(TileProject project, AutotileDefinition definition, Bitmap source, string? definitionRelativePath = null)
    {
        Normalize(definition);
        definition.ModifiedUtc = DateTimeOffset.UtcNow;
        definition.SourceSha256 = AutotileBitmap.ComputeSha256(source);
        var sourcePath = ProjectPath.Resolve(project, definition.SourceImageRelativePath);
        AutotileBitmap.SavePngAtomic(source, sourcePath);

        definitionRelativePath ??= project.Autotiles.FirstOrDefault(x => x.Id == definition.Id)?.RelativeDefinitionPath;
        if (string.IsNullOrWhiteSpace(definitionRelativePath))
            definitionRelativePath = Path.Combine("Autotiles", definition.Id.ToString("N"), "definition.autotile.json");
        var definitionPath = ProjectPath.Resolve(project, definitionRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(definitionPath)!);
        var temporary = definitionPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(definition, Options));
        File.Move(temporary, definitionPath, true);

        var reference = project.Autotiles.FirstOrDefault(x => x.Id == definition.Id);
        if (reference is not null)
        {
            reference.Name = definition.Name;
            reference.Revision = definition.Revision;
            reference.ModifiedUtc = definition.ModifiedUtc;
            reference.RelativeDefinitionPath = definitionRelativePath;
        }
    }

    public static void DeleteFiles(TileProject project, AutotileAssetReference reference)
    {
        var definitionPath = ProjectPath.Resolve(project, reference.RelativeDefinitionPath);
        var directory = Path.GetDirectoryName(definitionPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private static void Normalize(AutotileDefinition definition)
    {
        definition.Name ??= "Autotile";
        definition.Description ??= string.Empty;
        definition.SourceImageRelativePath ??= string.Empty;
        definition.SourceSha256 ??= string.Empty;
        definition.LastCompiledOutputSha256 ??= string.Empty;
        definition.Tags ??= new List<string>();
        if (definition.Revision < 1) definition.Revision = 1;
    }
}
