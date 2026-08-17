using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playroma.Sprite.Pixel;

public static class BrushPresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IReadOnlyList<BrushPreset> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) return [];
        try
        {
            PresetFile? file = JsonSerializer.Deserialize<PresetFile>(File.ReadAllText(path), Options);
            if (file?.Version != 1) return [];
            return file.Presets.Select(FromDto).ToArray();
        }
        catch (JsonException) { return []; }
        catch (IOException) { return []; }
        catch (InvalidDataException) { return []; }
        catch (ArgumentException) { return []; }
        catch (FormatException) { return []; }
        catch (OverflowException) { return []; }
    }

    public static void Save(string path, IEnumerable<BrushPreset> presets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(presets);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        PresetFile file = new() { Presets = presets.Select(ToDto).ToList() };
        File.WriteAllText(temporary, JsonSerializer.Serialize(file, Options));
        try { File.Move(temporary, fullPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static BrushPresetDto ToDto(BrushPreset preset)
    {
        PatternDto? pattern = null;
        if (preset.Pattern is not null)
        {
            pattern = new PatternDto
            {
                Name = preset.Pattern.Name,
                Width = preset.Pattern.Pixels.Width,
                Height = preset.Pattern.Pixels.Height,
                RgbaBase64 = Convert.ToBase64String(preset.Pattern.Pixels.Bytes.Span)
            };
        }
        return new BrushPresetDto
        {
            Name = preset.Name,
            BrushName = preset.Brush.Name,
            Width = preset.Brush.Width,
            Height = preset.Brush.Height,
            AnchorX = preset.Brush.AnchorX,
            AnchorY = preset.Brush.AnchorY,
            Shape = preset.Brush.Shape,
            Mask = preset.Brush.Mask.ToArray(),
            Pattern = pattern,
            Mode = preset.Mode,
            Opacity = preset.Opacity
        };
    }

    private static BrushPreset FromDto(BrushPresetDto dto)
    {
        BrushDefinition brush = new(dto.BrushName, dto.Width, dto.Height, dto.Mask, dto.AnchorX, dto.AnchorY, dto.Shape);
        BrushPattern? pattern = null;
        if (dto.Pattern is not null)
        {
            byte[] rgba = Convert.FromBase64String(dto.Pattern.RgbaBase64);
            pattern = new BrushPattern(dto.Pattern.Name, PixelBuffer.FromRgbaBytes(dto.Pattern.Width, dto.Pattern.Height, rgba));
        }
        return new BrushPreset(dto.Name, brush, pattern, dto.Mode, dto.Opacity);
    }

    private sealed class PresetFile
    {
        public int Version { get; set; } = 1;
        public List<BrushPresetDto> Presets { get; set; } = [];
    }

    private sealed class BrushPresetDto
    {
        public string Name { get; set; } = "Brush";
        public string BrushName { get; set; } = "Custom";
        public int Width { get; set; }
        public int Height { get; set; }
        public int AnchorX { get; set; }
        public int AnchorY { get; set; }
        public BrushShape Shape { get; set; } = BrushShape.Custom;
        public bool[] Mask { get; set; } = [];
        public PatternDto? Pattern { get; set; }
        public InkMode Mode { get; set; }
        public byte Opacity { get; set; } = 255;
    }

    private sealed class PatternDto
    {
        public string Name { get; set; } = "Pattern";
        public int Width { get; set; }
        public int Height { get; set; }
        public string RgbaBase64 { get; set; } = string.Empty;
    }
}
