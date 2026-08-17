using System.Text.Json;

namespace Playroma.Sprite.Automation;

public sealed class ExportPresetStore
{
    private readonly string directory;
    public ExportPresetStore(string directory) { this.directory = Path.GetFullPath(directory); }
    public string DirectoryPath => directory;

    public IReadOnlyList<(string Path, SpriteExportPreset Preset, string? Error)> LoadAll()
    {
        if (!Directory.Exists(directory)) return [];
        List<(string, SpriteExportPreset, string?)> result = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.playroma-export.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                SpriteExportPreset preset = JsonSerializer.Deserialize<SpriteExportPreset>(File.ReadAllText(path), AutomationJson.Options)
                    ?? throw new InvalidDataException("The preset file is empty.");
                Validate(preset); result.Add((path, preset, null));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            { result.Add((path, new SpriteExportPreset { Name = Path.GetFileNameWithoutExtension(path) }, exception.Message)); }
        }
        return result.OrderBy(item => item.Item2.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string Save(SpriteExportPreset preset)
    {
        Validate(preset); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, AutomationVariables.Sanitize(preset.Name) + ".playroma-export.json");
        string temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(preset, AutomationJson.Options)); File.Move(temporary, path, true); return path;
    }

    public static void Validate(SpriteExportPreset preset)
    {
        if (preset.Schema != "playroma-sprite-export-preset/1") throw new InvalidDataException("Unsupported export preset schema.");
        if (string.IsNullOrWhiteSpace(preset.Name)) throw new InvalidDataException("Export preset name is required.");
        if (string.IsNullOrWhiteSpace(preset.Format)) throw new InvalidDataException("Export format is required.");
        if (string.IsNullOrWhiteSpace(preset.OutputPath)) throw new InvalidDataException("Export outputPath is required.");
        if (preset.Scale is < 1 or > 64) throw new InvalidDataException("Export scale must be between 1 and 64.");
        if (preset.Padding is < 0 or > 256) throw new InvalidDataException("Export padding must be between 0 and 256.");
        if (preset.MaximumWidth is < 1 or > 32768 || preset.MaximumHeight is < 1 or > 32768) throw new InvalidDataException("Atlas maximum dimensions must be between 1 and 32768.");
    }
}
