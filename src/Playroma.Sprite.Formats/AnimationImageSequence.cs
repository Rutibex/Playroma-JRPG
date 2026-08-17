using System.Globalization;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public sealed record AnimationFrameImage(PixelBuffer Pixels, int DurationMilliseconds);

public static class AnimationImageSequence
{
    public static IReadOnlyList<AnimationFrameImage> Load(IEnumerable<string> paths, int defaultDurationMilliseconds = 100)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (defaultDurationMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(defaultDurationMilliseconds));
        string[] ordered = paths.Select(Path.GetFullPath).OrderBy(NaturalSortKey, StringComparer.OrdinalIgnoreCase).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (ordered.Length == 0) throw new ArgumentException("At least one PNG frame is required.", nameof(paths));
        AnimationFrameImage[] frames = ordered.Select(path => new AnimationFrameImage(PngCodec.Load(path), defaultDurationMilliseconds)).ToArray();
        int width = frames[0].Pixels.Width, height = frames[0].Pixels.Height;
        if (frames.Any(frame => frame.Pixels.Width != width || frame.Pixels.Height != height))
            throw new InvalidDataException("Every PNG sequence frame must have identical dimensions.");
        return frames;
    }

    public static IReadOnlyList<AnimationFrameImage> LoadDirectory(string directory, string searchPattern = "*.png", int defaultDurationMilliseconds = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Load(Directory.EnumerateFiles(directory, searchPattern).OrderBy(NaturalSortKey, StringComparer.OrdinalIgnoreCase), defaultDurationMilliseconds);
    }

    public static IReadOnlyList<string> Save(IReadOnlyList<AnimationFrameImage> frames, string directory, string baseName, int firstNumber = 0, int digits = 4)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        if (digits is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(digits));
        Directory.CreateDirectory(directory);
        List<string> paths = [];
        for (int index = 0; index < frames.Count; index++)
        {
            string path = Path.Combine(directory, $"{baseName}_{(firstNumber + index).ToString(new string('0', digits), CultureInfo.InvariantCulture)}.png");
            PngCodec.Save(frames[index].Pixels, path);
            paths.Add(path);
        }
        return paths;
    }

    private static string NaturalSortKey(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        System.Text.StringBuilder result = new();
        for (int index = 0; index < name.Length;)
        {
            if (!char.IsDigit(name[index])) { result.Append(char.ToUpperInvariant(name[index++])); continue; }
            int start = index;
            while (index < name.Length && char.IsDigit(name[index])) index++;
            string number = name[start..index].TrimStart('0');
            if (number.Length == 0) number = "0";
            result.Append('#').Append(number.Length.ToString("D4", CultureInfo.InvariantCulture)).Append(number);
        }
        return result.ToString();
    }
}
