using Playroma.JRPG.Core;

namespace Playroma.JRPG.Assets;

public sealed record AssetMetadataResult(
    AssetMetadataSummary Metadata,
    IReadOnlyList<ProjectDiagnostic> Diagnostics)
{
    public static AssetMetadataResult Empty { get; } = new(AssetMetadataSummary.Empty, []);
}

public sealed record AssetThumbnailRequest(
    int Width,
    int Height,
    string Recipe = "default",
    bool PreservePixelEdges = true)
{
    public int EffectiveWidth => Math.Clamp(Width, 16, 2048);
    public int EffectiveHeight => Math.Clamp(Height, 16, 2048);
}

public sealed record AssetThumbnail(
    byte[] PngBytes,
    int Width,
    int Height,
    string CacheIdentity,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

public interface IAssetMetadataProvider
{
    string Id { get; }
    int Version { get; }
    bool CanHandle(AssetRecord asset, AssetVariant variant);
    ValueTask<AssetMetadataResult> ReadAsync(
        AssetRecord asset,
        AssetVariant variant,
        CancellationToken cancellationToken = default);
}

public interface IAssetThumbnailProvider
{
    string Id { get; }
    int Version { get; }
    bool CanHandle(AssetRecord asset, AssetVariant variant);
    ValueTask<AssetThumbnail> CreateAsync(
        AssetRecord asset,
        AssetVariant variant,
        AssetThumbnailRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAudioWaveformProvider
{
    bool CanHandle(AssetRecord asset, AssetVariant variant);
    ValueTask<WaveformEnvelope> CreateAsync(
        AssetRecord asset,
        AssetVariant variant,
        int bucketCount,
        CancellationToken cancellationToken = default);
}

public interface IAssetContentSource
{
    string ProjectRoot { get; }
    ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);
    string GetFullPath(string relativePath);
}

public interface IAssetThumbnailService : IAsyncDisposable
{
    ValueTask<AssetThumbnail> GetThumbnailAsync(
        AssetRecord asset,
        AssetVariant variant,
        AssetThumbnailRequest request,
        CancellationToken cancellationToken = default);

    void Invalidate(string relativePath);
}

public enum AssetContentOverlayKind
{
    Create,
    Replace,
    Move,
    Delete
}

public sealed record AssetContentOverlayEntry(
    AssetContentOverlayKind Kind,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    string? ContentPath,
    long ContentLength,
    DateTimeOffset LastWriteTimeUtc);

public interface IAssetOverlayContentSource : IAssetContentSource
{
    IReadOnlyList<AssetContentOverlayEntry> PendingEntries { get; }
    bool Exists(string relativePath);
}

public sealed class PhysicalAssetContentSource : IAssetContentSource
{
    private readonly string _root;

    public PhysicalAssetContentSource(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        _root = Path.GetFullPath(projectRoot);
    }

    public string ProjectRoot => _root;

    public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            GetFullPath(relativePath),
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return ValueTask.FromResult(stream);
    }

    public string GetFullPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string combined = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && !string.Equals(combined, _root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The asset path escapes the project root.");
        }
        return combined;
    }
}
