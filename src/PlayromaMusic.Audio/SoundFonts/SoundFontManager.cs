using System.Text;
using PlayromaMusic.Core.Orchestration;

namespace PlayromaMusic.Audio.SoundFonts;

public sealed class SoundFontManager
{
    private readonly List<string> _searchPaths = new();

    public SoundFontManager(IEnumerable<string>? searchPaths = null)
    {
        if (searchPaths is not null)
        {
            foreach (string path in searchPaths)
            {
                AddSearchPath(path);
            }
        }
    }

    public IReadOnlyList<string> SearchPaths => _searchPaths;
    public IReadOnlyList<SoundFontDescriptor> SoundFonts { get; private set; } = Array.Empty<SoundFontDescriptor>();

    public void AddSearchPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!_searchPaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            _searchPaths.Add(fullPath);
        }
    }

    public IReadOnlyList<SoundFontDescriptor> Scan()
    {
        List<SoundFontDescriptor> results = new();
        HashSet<string> found = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in _searchPaths)
        {
            if (!Directory.Exists(path))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(IsSoundFontExtension)
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string file in files)
            {
                string fullPath = Path.GetFullPath(file);
                if (!found.Add(fullPath))
                {
                    continue;
                }

                FileInfo info = new(fullPath);
                SoundFontFormat format = info.Extension.Equals(".sf3", StringComparison.OrdinalIgnoreCase)
                    ? SoundFontFormat.Sf3
                    : SoundFontFormat.Sf2;
                results.Add(new SoundFontDescriptor(
                    Path.GetFileNameWithoutExtension(info.Name),
                    fullPath,
                    format,
                    info.Length,
                    info.LastWriteTimeUtc,
                    HasSoundFontHeader(fullPath)));
            }
        }

        SoundFonts = results
            .OrderByDescending(item => item.HeaderValid)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return SoundFonts;
    }

    public InstrumentCatalogDocument ReadPresetCatalog(string path) =>
        new SoundFontPresetCatalogReader().Read(path);

    public InstrumentCatalogDocument ReadAndProfilePresetCatalog(
        string path,
        int maximumPresets = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        InstrumentCatalogDocument catalog = ReadPresetCatalog(path);
        return new PresetAudioProfiler().Profile(path, catalog, maximumPresets, cancellationToken);
    }

    public SoundFontDescriptor? Find(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string fullPath = Path.GetFullPath(path);
        return SoundFonts.FirstOrDefault(item =>
            string.Equals(item.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSoundFontExtension(string path) =>
        Path.GetExtension(path).Equals(".sf2", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".sf3", StringComparison.OrdinalIgnoreCase);

    public static bool HasSoundFontHeader(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[12];
            using FileStream stream = File.OpenRead(path);
            if (stream.Read(header) != header.Length)
            {
                return false;
            }

            return Encoding.ASCII.GetString(header[..4]) == "RIFF" &&
                   Encoding.ASCII.GetString(header[8..12]) == "sfbk";
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static IReadOnlyList<string> CreateDefaultSearchPaths(string applicationDirectory, string userSoundFontDirectory)
    {
        List<string> paths =
        [
            Path.Combine(applicationDirectory, "SoundFonts"),
            userSoundFontDirectory
        ];

        string? configured = Environment.GetEnvironmentVariable("PROCEDURAL_MIDI_SOUNDFONTS");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            paths.AddRange(configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return paths;
    }
}
