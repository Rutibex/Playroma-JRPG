namespace PlayromaMusic.Audio.SoundFonts;

public enum SoundFontFormat
{
    Sf2,
    Sf3
}

public sealed record SoundFontDescriptor(
    string Name,
    string FullPath,
    SoundFontFormat Format,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    bool HeaderValid)
{
    public string DisplayName => $"{Name} ({Format.ToString().ToUpperInvariant()}, {FormatSize(SizeBytes)})";

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
