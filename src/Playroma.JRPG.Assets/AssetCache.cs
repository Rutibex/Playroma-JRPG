using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Playroma.JRPG.Assets;

public sealed record AssetCacheOptions(
    long MaximumThumbnailBytes = 512L * 1024 * 1024,
    long MaximumWaveformBytes = 256L * 1024 * 1024,
    long MaximumMetadataBytes = 64L * 1024 * 1024);

public sealed class AssetCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _root;
    private readonly AssetCacheOptions _options;

    public AssetCache(string projectRoot, AssetCacheOptions? options = null, string? cacheRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(projectRoot).ToUpperInvariant()))).ToLowerInvariant();
        _root = cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playroma", "JRPG", "cache", "projects", identity, "assets");
        _options = options ?? new AssetCacheOptions();
    }

    public string Root => _root;

    public async ValueTask<AssetMetadataSummary?> TryReadMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        string path = GetPath("metadata", key, ".json");
        if (!File.Exists(path)) return null;
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AssetMetadataSummary>(bytes, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            TryDelete(path);
            return null;
        }
    }

    public async ValueTask WriteMetadataAsync(string key, AssetMetadataSummary metadata, CancellationToken cancellationToken = default)
    {
        string path = GetPath("metadata", key, ".json");
        await WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions), cancellationToken).ConfigureAwait(false);
        ApplyQuota(Path.Combine(_root, "metadata"), _options.MaximumMetadataBytes);
    }

    public async ValueTask<byte[]?> TryReadThumbnailAsync(string key, CancellationToken cancellationToken = default)
    {
        string path = GetPath("thumbnails", key, ".png");
        try { return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false) : null; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { TryDelete(path); return null; }
    }

    public async ValueTask WriteThumbnailAsync(string key, byte[] bytes, CancellationToken cancellationToken = default)
    {
        string path = GetPath("thumbnails", key, ".png");
        await WriteAtomicAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        ApplyQuota(Path.Combine(_root, "thumbnails"), _options.MaximumThumbnailBytes);
    }

    public async ValueTask<WaveformEnvelope?> TryReadWaveformAsync(string key, CancellationToken cancellationToken = default)
    {
        string path = GetPath("waveforms", key, ".json");
        if (!File.Exists(path)) return null;
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<WaveformEnvelope>(bytes, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { TryDelete(path); return null; }
    }

    public async ValueTask WriteWaveformAsync(string key, WaveformEnvelope waveform, CancellationToken cancellationToken = default)
    {
        string path = GetPath("waveforms", key, ".json");
        await WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(waveform, JsonOptions), cancellationToken).ConfigureAwait(false);
        ApplyQuota(Path.Combine(_root, "waveforms"), _options.MaximumWaveformBytes);
    }

    public static string BuildKey(AssetVariant variant, string recipe, int processorVersion = 1)
    {
        string material = $"{variant.NormalizedRelativePath}|{variant.Size}|{variant.LastWriteTimeUtc.UtcDateTime.Ticks}|{variant.Sha256}|{recipe}|{processorVersion}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private string GetPath(string category, string key, string extension) => Path.Combine(_root, category, key + extension);

    private static async ValueTask WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Cache path has no directory."));
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static void ApplyQuota(string directory, long maximumBytes)
    {
        if (!Directory.Exists(directory) || maximumBytes <= 0) return;
        FileInfo[] files = new DirectoryInfo(directory).EnumerateFiles().OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        long retained = 0;
        foreach (FileInfo file in files)
        {
            retained += file.Length;
            if (retained <= maximumBytes) continue;
            try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
