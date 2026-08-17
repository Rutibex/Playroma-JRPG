using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Tile.Core.Models;

namespace Playroma.Tile.Core.Services;

public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TileProject CreateNew(string projectFilePath, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);

        var fullPath = Path.GetFullPath(projectFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var project = new TileProject
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? Path.GetFileNameWithoutExtension(fullPath)
                : name.Trim(),
            ProjectFilePath = fullPath
        };

        Save(project);
        return project;
    }

    public static TileProject Load(string projectFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        var fullPath = Path.GetFullPath(projectFilePath);
        var json = File.ReadAllText(fullPath);
        var project = JsonSerializer.Deserialize<TileProject>(json, Options)
                      ?? throw new InvalidDataException("The project file did not contain a valid project.");

        if (project.FormatVersion > 4)
        {
            throw new InvalidDataException(
                $"This project uses format version {project.FormatVersion}, but this build supports version 4.");
        }

        project.ProjectFilePath = fullPath;
        if (project.FormatVersion < 4) project.FormatVersion = 4;
        Normalize(project);
        return project;
    }

    public static void Save(TileProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(project.ProjectFilePath);

        Normalize(project);
        project.ModifiedUtc = DateTimeOffset.UtcNow;

        var fullPath = Path.GetFullPath(project.ProjectFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        var json = JsonSerializer.Serialize(project, Options);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, fullPath, true);
    }

    public static string ToJson(TileProject project)
    {
        Normalize(project);
        return JsonSerializer.Serialize(project, Options);
    }

    private static void Normalize(TileProject project)
    {
        project.TileSets ??= new List<TileSetDefinition>();
        project.Autotiles ??= new List<AutotileAssetReference>();
        project.Materials ??= new List<MaterialAssetReference>();
        project.TilesetConstructions ??= new List<TilesetConstructionReference>();
        foreach (var material in project.Materials)
        {
            material.Name ??= "Material";
            material.RelativeRecipePath ??= string.Empty;
            material.RelativeThumbnailPath ??= string.Empty;
            if (material.Revision < 1) material.Revision = 1;
        }
        foreach (var autotile in project.Autotiles)
        {
            autotile.Name ??= "Autotile";
            autotile.RelativeDefinitionPath ??= string.Empty;
            if (autotile.Revision < 1) autotile.Revision = 1;
        }

        foreach (var construction in project.TilesetConstructions)
        {
            construction.Name ??= "Tileset Construction";
            construction.RelativeDefinitionPath ??= string.Empty;
            construction.RelativeThumbnailPath ??= string.Empty;
            if (construction.Revision < 1) construction.Revision = 1;
        }

        foreach (var tileSet in project.TileSets)
        {
            tileSet.Name ??= "Tileset";
            tileSet.Description ??= string.Empty;
            tileSet.MvDatabaseProjectPath ??= string.Empty;
            tileSet.Sheets ??= new List<TileSheetDefinition>();

            foreach (var sheet in tileSet.Sheets)
            {
                sheet.DisplayName ??= string.Empty;
                sheet.FileName ??= string.Empty;
                sheet.RelativeImagePath ??= string.Empty;
                sheet.SourceFileName ??= string.Empty;
                sheet.SourceSha256 ??= string.Empty;
                sheet.Regions ??= new List<TileRegionMetadata>();

                foreach (var region in sheet.Regions)
                {
                    region.Name ??= string.Empty;
                    region.AlternateName ??= string.Empty;
                    region.Notes ??= string.Empty;
                    region.Tags ??= new List<string>();
                    region.CompiledSourceSha256 ??= string.Empty;
                    region.CompiledOutputSha256 ??= string.Empty;
                }
            }
        }
    }
}
