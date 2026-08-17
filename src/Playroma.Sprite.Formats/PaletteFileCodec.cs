using System.Globalization;
using System.Text;
using System.Text.Json;
using Playroma.Sprite.Core;

namespace Playroma.Sprite.Formats;

public static class PaletteFileCodec
{
    public static SpritePalette Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string text = File.ReadAllText(path);
        SpritePalette palette = extension switch
        {
            ".gpl" => ParseGpl(text),
            ".pal" => ParseJasc(text),
            ".hex" or ".txt" => ParseHex(text),
            ".json" => ParseJson(text),
            _ => throw new NotSupportedException($"Palette format '{extension}' is not supported.")
        };
        palette.Name = string.IsNullOrWhiteSpace(palette.Name) ? Path.GetFileNameWithoutExtension(path) : palette.Name;
        palette.SourceFormat = extension.TrimStart('.');
        return palette;
    }

    public static void Save(SpritePalette palette, string path)
    {
        ArgumentNullException.ThrowIfNull(palette); ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string text = extension switch
        {
            ".gpl" => WriteGpl(palette),
            ".pal" => WriteJasc(palette),
            ".hex" or ".txt" => WriteHex(palette),
            ".json" => JsonSerializer.Serialize(palette, SpriteDocumentJsonSerializer.Options),
            _ => throw new NotSupportedException($"Palette format '{extension}' is not supported.")
        };
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static SpritePalette ParseGpl(string text)
    {
        string[] lines = Normalize(text); if (lines.Length == 0 || !lines[0].StartsWith("GIMP Palette", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Not a GIMP GPL palette.");
        SpritePalette palette = new() { Name = "Imported GPL" };
        foreach (string raw in lines.Skip(1))
        {
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase)) { palette.Name = line[5..].Trim(); continue; }
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries); if (parts.Length < 3) continue;
            if (!byte.TryParse(parts[0], out byte r) || !byte.TryParse(parts[1], out byte g) || !byte.TryParse(parts[2], out byte b)) continue;
            palette.Entries.Add(new SpritePaletteEntry { Index = palette.Entries.Count, Name = parts.Length > 3 ? string.Join(' ', parts.Skip(3)) : null, Color = new(r, g, b, 255) });
        }
        Validate(palette); return palette;
    }

    private static SpritePalette ParseJasc(string text)
    {
        string[] lines = Normalize(text).Where(line => line.Trim().Length > 0).ToArray();
        if (lines.Length < 3 || !lines[0].Equals("JASC-PAL", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Not a JASC PAL palette.");
        SpritePalette palette = new() { Name = "Imported JASC" };
        foreach (string line in lines.Skip(3))
        {
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries); if (parts.Length < 3) continue;
            palette.Entries.Add(new SpritePaletteEntry { Index = palette.Entries.Count, Color = new(byte.Parse(parts[0], CultureInfo.InvariantCulture), byte.Parse(parts[1], CultureInfo.InvariantCulture), byte.Parse(parts[2], CultureInfo.InvariantCulture), 255) });
        }
        Validate(palette); return palette;
    }

    private static SpritePalette ParseHex(string text)
    {
        SpritePalette palette = new() { Name = "Imported HEX" };
        foreach (string raw in Normalize(text))
        {
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith("//") || line.StartsWith(';')) continue;
            string token = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].TrimStart('#');
            if (token.Length is not (6 or 8) || !uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)) continue;
            byte r, g, b, a;
            if (token.Length == 6) { r = (byte)(value >> 16); g = (byte)(value >> 8); b = (byte)value; a = 255; }
            else { r = (byte)(value >> 24); g = (byte)(value >> 16); b = (byte)(value >> 8); a = (byte)value; }
            palette.Entries.Add(new SpritePaletteEntry { Index = palette.Entries.Count, Color = new(r, g, b, a) });
        }
        Validate(palette); return palette;
    }

    private static SpritePalette ParseJson(string text)
    {
        SpritePalette palette = JsonSerializer.Deserialize<SpritePalette>(text, SpriteDocumentJsonSerializer.Options) ?? throw new InvalidDataException("Palette JSON is empty.");
        Validate(palette); return palette;
    }

    private static string WriteGpl(SpritePalette palette)
    {
        StringBuilder builder = new(); builder.AppendLine("GIMP Palette"); builder.AppendLine("Name: " + palette.Name); builder.AppendLine("Columns: 8"); builder.AppendLine("# Exported by Playroma Sprite Studio");
        foreach (SpritePaletteEntry entry in Ordered(palette)) builder.AppendLine($"{entry.Color.R,3} {entry.Color.G,3} {entry.Color.B,3}\t{entry.Name ?? $"Color {entry.Index}"}");
        return builder.ToString();
    }

    private static string WriteJasc(SpritePalette palette)
    {
        SpritePaletteEntry[] entries = Ordered(palette).Take(256).ToArray(); StringBuilder builder = new(); builder.AppendLine("JASC-PAL"); builder.AppendLine("0100"); builder.AppendLine(entries.Length.ToString(CultureInfo.InvariantCulture));
        foreach (SpritePaletteEntry entry in entries) builder.AppendLine($"{entry.Color.R} {entry.Color.G} {entry.Color.B}"); return builder.ToString();
    }

    private static string WriteHex(SpritePalette palette)
    {
        StringBuilder builder = new(); foreach (SpritePaletteEntry entry in Ordered(palette)) builder.AppendLine(entry.Color.A == 255 ? $"#{entry.Color.R:X2}{entry.Color.G:X2}{entry.Color.B:X2}" : $"#{entry.Color.R:X2}{entry.Color.G:X2}{entry.Color.B:X2}{entry.Color.A:X2}"); return builder.ToString();
    }

    private static SpritePaletteEntry[] Ordered(SpritePalette palette) => palette.Entries.OrderBy(entry => entry.Index).ToArray();
    private static string[] Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    private static void Validate(SpritePalette palette)
    {
        if (palette.Entries.Count == 0) throw new InvalidDataException("The palette contains no colors.");
        if (palette.Entries.Count > 256) throw new InvalidDataException("Indexed palettes cannot contain more than 256 colors.");
        for (int index = 0; index < palette.Entries.Count; index++) palette.Entries[index].Index = index;
    }
}
