using System.IO.Compression;
using System.Text;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyBundleSourceReader
{
    public IReadOnlyList<YanflyCanonicalPluginSource> ReadCanonicalSources(
        string inputPath, YanflyBundleInventory inventory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(inventory);
        Dictionary<string, byte[]> files = ReadFiles(inputPath);
        List<YanflyCanonicalPluginSource> result = [];
        foreach (YanflyPluginInventoryEntry plugin in inventory.Plugins
                     .OrderBy(p => p.PluginId, StringComparer.OrdinalIgnoreCase))
        {
            string path = Normalize(plugin.CanonicalPath);
            if (!files.TryGetValue(path, out byte[]? bytes))
                throw new FileNotFoundException($"Canonical source for {plugin.PluginId} was not found: {path}");
            result.Add(new(plugin.PluginId, plugin.FileName, path, plugin.Version ?? string.Empty,
                plugin.CanonicalSha256, Decode(bytes), plugin));
        }
        return result;
    }

    private static Dictionary<string, byte[]> ReadFiles(string inputPath)
    {
        Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(inputPath))
        {
            string root = Path.GetFullPath(inputPath);
            foreach (string path in Directory.EnumerateFiles(root, "*.js", SearchOption.AllDirectories))
                files[Normalize(Path.GetRelativePath(root, path))] = File.ReadAllBytes(path);
            return files;
        }
        if (!File.Exists(inputPath)) throw new FileNotFoundException("YEP bundle not found.", inputPath);
        if (!Path.GetExtension(inputPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("YEP bundle must be a directory or ZIP archive.");
        using ZipArchive archive = ZipFile.OpenRead(inputPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) continue;
            using Stream input = entry.Open();
            using MemoryStream output = new();
            input.CopyTo(output);
            files[Normalize(entry.FullName)] = output.ToArray();
        }
        return files;
    }

    private static string Decode(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
