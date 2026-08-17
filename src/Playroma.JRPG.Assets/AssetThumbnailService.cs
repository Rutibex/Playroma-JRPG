using System.Collections.Concurrent;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Assets;

/// <summary>
/// Bounded, coalescing thumbnail service. Providers decode only requested assets,
/// and the completed PNG bytes are cached outside the project tree.
/// </summary>
public sealed class AssetThumbnailService : IAssetThumbnailService
{
    private readonly AssetCache _cache;
    private readonly IReadOnlyList<IAssetThumbnailProvider> _providers;
    private readonly SemaphoreSlim _decodeGate;
    private readonly ConcurrentDictionary<string, Lazy<Task<AssetThumbnail>>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _invalidatedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    public AssetThumbnailService(
        AssetCache cache,
        IEnumerable<IAssetThumbnailProvider> providers,
        int maximumConcurrency = 3)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
        _decodeGate = new SemaphoreSlim(Math.Clamp(maximumConcurrency, 1, 16));
    }

    public async ValueTask<AssetThumbnail> GetThumbnailAsync(
        AssetRecord asset,
        AssetVariant variant,
        AssetThumbnailRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(request);

        IAssetThumbnailProvider? provider = _providers.FirstOrDefault(candidate => candidate.CanHandle(asset, variant));
        if (provider is null)
        {
            return Unsupported(variant, "No thumbnail provider supports this asset format.");
        }

        string cacheKey = AssetCache.BuildKey(
            variant,
            $"thumbnail:{provider.Id}:{request.Recipe}:{request.EffectiveWidth}x{request.EffectiveHeight}:{request.PreservePixelEdges}",
            provider.Version);

        bool invalidated = _invalidatedPaths.TryRemove(variant.NormalizedRelativePath, out _);
        if (!invalidated)
        {
            byte[]? cached = await _cache.TryReadThumbnailAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return new AssetThumbnail(cached, request.EffectiveWidth, request.EffectiveHeight, cacheKey, []);
            }
        }

        Lazy<Task<AssetThumbnail>> lazy = _inflight.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<AssetThumbnail>>(
                () => DecodeAndCacheAsync(provider, asset, variant, request, cacheKey, _shutdown.Token),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inflight.TryRemove(cacheKey, out _);
        }
    }

    public void Invalidate(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        _invalidatedPaths[relativePath.Replace('\\', '/')] = 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        Task[] active = _inflight.Values
            .Where(lazy => lazy.IsValueCreated)
            .Select(lazy => lazy.Value)
            .ToArray();
        try { await Task.WhenAll(active).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        _decodeGate.Dispose();
        _shutdown.Dispose();
        _inflight.Clear();
        _invalidatedPaths.Clear();
    }

    private async Task<AssetThumbnail> DecodeAndCacheAsync(
        IAssetThumbnailProvider provider,
        AssetRecord asset,
        AssetVariant variant,
        AssetThumbnailRequest request,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        await _decodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AssetThumbnail thumbnail = await provider.CreateAsync(asset, variant, request, cancellationToken).ConfigureAwait(false);
            if (thumbnail.PngBytes.Length > 0)
            {
                await _cache.WriteThumbnailAsync(cacheKey, thumbnail.PngBytes, cancellationToken).ConfigureAwait(false);
            }

            return thumbnail with { CacheIdentity = cacheKey };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return Unsupported(variant, exception.Message);
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    private static AssetThumbnail Unsupported(AssetVariant variant, string detail) => new(
        [],
        0,
        0,
        string.Empty,
        [ProjectDiagnostic.Warning(
            DiagnosticCodes.Phase5UnsupportedPreview,
            "The asset could not be previewed.",
            new ProjectLocation(variant.RelativePath),
            detail)]);
}
