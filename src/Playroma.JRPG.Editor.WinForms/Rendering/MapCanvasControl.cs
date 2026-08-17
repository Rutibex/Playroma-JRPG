using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using Playroma.JRPG.Editor.WinForms;
using Playroma.JRPG.Rendering;
using Playroma.JRPG.Rendering.Windows;

namespace Playroma.JRPG.Editor.WinForms.Rendering;

[Flags]
public enum MapOverlayOptions
{
    None = 0,
    Grid = 1,
    Regions = 2,
    Shadows = 4,
    Passability = 8,
    EventIds = 16,
    EventNames = 32,
    TerrainTags = 64,
    EventPriorities = 128
}

public sealed class MapCoordinateEventArgs : EventArgs
{
    public MapCoordinateEventArgs(int x, int y, int regionId, int shadowBits, int? eventId)
    {
        X = x;
        Y = y;
        RegionId = regionId;
        ShadowBits = shadowBits;
        EventId = eventId;
    }

    public int X { get; }
    public int Y { get; }
    public int RegionId { get; }
    public int ShadowBits { get; }
    public int? EventId { get; }
}

public sealed class MapTilePointerEventArgs : EventArgs
{
    public MapTilePointerEventArgs(int x, int y, MouseButtons button, Keys modifiers, Point clientLocation)
    {
        X = x;
        Y = y;
        Button = button;
        Modifiers = modifiers;
        ClientLocation = clientLocation;
    }

    public int X { get; }
    public int Y { get; }
    public MouseButtons Button { get; }
    public Keys Modifiers { get; }
    public Point ClientLocation { get; }
}

/// <summary>Accelerated, chunk-cached MV map canvas with a non-destructive Phase 7 edit-preview pass.</summary>
public sealed class MapCanvasControl : ScrollableControl
{
    private const int ChunkTiles = 16;
    private const int MaximumCachedChunks = 96;
    private readonly Dictionary<ChunkKey, CacheEntry> _cache = [];
    private readonly ConcurrentDictionary<ChunkKey, byte> _pending = new();
    private readonly SemaphoreSlim _renderSemaphore = new(2, 2);
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 500 };
    private CancellationTokenSource _renderCancellation = new();
    private CancellationTokenSource _previewCancellation = new();
    private LoadedMapRenderContext? _context;
    private Bitmap? _previewBitmap;
    private PixelRect _previewRegion;
    private PixelRect? _selectionRegion;
    private float _zoom = 1f;
    private int _animationFrame;
    private int? _pendingAnimationFrame;
    private int _renderGeneration;
    private long _accessCounter;
    private bool _middleDragging;
    private Point _dragStart;
    private Point _dragScrollStart;
    private Point? _hoverTile;
    private Point _keyboardTile;
    private bool _disposed;
    private MapOverlayOptions _overlays = MapOverlayOptions.Grid | MapOverlayOptions.EventIds;
    private bool _animationEnabled = true;
    private Color _gridColor = Color.FromArgb(80, 255, 255, 255);
    private Color _passabilityColor = Color.FromArgb(220, 230, 68, 68);
    private Color _shadowColor = Color.FromArgb(230, 255, 193, 7);
    private Color _labelBackground = Color.FromArgb(180, 20, 20, 20);
    private Color _checkerboardLight = Color.FromArgb(44, 46, 50);
    private Color _checkerboardDark = Color.FromArgb(52, 54, 58);
    private Color _focusColor = SystemColors.Highlight;

    public MapCanvasControl()
    {
        AutoScroll = true;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(29, 31, 34);
        ForeColor = SystemColors.WindowText;
        TabStop = true;
        AccessibleName = "Map canvas";
        AccessibleDescription = "No map is loaded. Use the arrow keys to move the map cursor after a map opens.";
        AccessibleRole = AccessibleRole.Pane;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        _animationTimer.Tick += (_, _) => AdvanceAnimation();
    }

    public void ApplyPalette(ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        BackColor = palette.CanvasBackground;
        ForeColor = palette.PrimaryText;
        _gridColor = palette.HighContrast ? SystemColors.WindowText : Color.FromArgb(92, palette.PrimaryText);
        _passabilityColor = palette.HighContrast ? SystemColors.Highlight : palette.Error;
        _shadowColor = palette.HighContrast ? SystemColors.HotTrack : palette.Warning;
        _labelBackground = palette.HighContrast ? SystemColors.Window : Color.FromArgb(210, palette.EditorBackground);
        _checkerboardLight = palette.CheckerboardLight;
        _checkerboardDark = palette.CheckerboardDark;
        _focusColor = palette.FocusIndicator;
        Invalidate();
    }

    public event EventHandler<MapCoordinateEventArgs>? CoordinateChanged;
    public event EventHandler<MapTilePointerEventArgs>? TilePointerDown;
    public event EventHandler<MapTilePointerEventArgs>? TilePointerMove;
    public event EventHandler<MapTilePointerEventArgs>? TilePointerUp;
    public event EventHandler<MapTilePointerEventArgs>? TileDoubleClicked;
    public event EventHandler? ZoomChanged;
    public event EventHandler? ViewportChanged;

    public float Zoom => _zoom;
    public int AnimationFrame => _animationFrame;
    public PixelRect VisibleMapRegion
    {
        get
        {
            if (_context is null) return new PixelRect(0, 0, 0, 0);
            return PixelRect.Intersect(new PixelRect(0, 0, _context.Map.PixelSize.Width, _context.Map.PixelSize.Height), VisibleMapPixelBounds(CurrentScroll));
        }
    }
    public bool AnimationEnabled
    {
        get => _animationEnabled;
        set
        {
            if (_animationEnabled == value) return;
            _animationEnabled = value;
            _animationTimer.Enabled = value && _context is not null && ContainsAnimatedTiles(_context.Map);
            if (!value)
            {
                _animationFrame = 0;
                _pendingAnimationFrame = null;
            }
            Invalidate();
        }
    }
    public MapOverlayOptions Overlays
    {
        get => _overlays;
        set
        {
            if (_overlays == value) return;
            _overlays = value;
            Invalidate();
        }
    }

    public LoadedMapRenderContext? Context => _context;

    public PixelRect? SelectionRegion
    {
        get => _selectionRegion;
        set
        {
            if (_selectionRegion == value) return;
            _selectionRegion = value;
            Invalidate();
        }
    }

    public bool TryClientToMapTile(Point clientPoint, out Point tile)
    {
        tile = Point.Empty;
        LoadedMapRenderContext? context = _context;
        if (context is null) return false;
        Point scroll = CurrentScroll;
        int mapPixelX = checked((int)Math.Floor((scroll.X + clientPoint.X) / _zoom));
        int mapPixelY = checked((int)Math.Floor((scroll.Y + clientPoint.Y) / _zoom));
        int x = mapPixelX / MvTileRules.TileWidth;
        int y = mapPixelY / MvTileRules.TileHeight;
        if (x < 0 || y < 0 || x >= context.Map.Width || y >= context.Map.Height) return false;
        tile = new Point(x, y);
        return true;
    }

    public void ClearEditPreview()
    {
        _previewCancellation.Cancel();
        _previewCancellation.Dispose();
        _previewCancellation = new CancellationTokenSource();
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _previewRegion = default;
        Invalidate();
    }

    public void SetEditPreview(MapRenderSnapshot previewMap, PixelRect region)
    {
        ArgumentNullException.ThrowIfNull(previewMap);
        LoadedMapRenderContext? context = _context;
        if (context is null) return;
        PixelRect clipped = PixelRect.Intersect(
            new PixelRect(0, 0, previewMap.PixelSize.Width, previewMap.PixelSize.Height),
            region);
        if (clipped.IsEmpty)
        {
            ClearEditPreview();
            return;
        }
        _previewCancellation.Cancel();
        _previewCancellation.Dispose();
        _previewCancellation = new CancellationTokenSource();
        CancellationToken token = _previewCancellation.Token;
        _ = RenderEditPreviewAsync(context, previewMap, clipped, token);
    }

    public void UpdateMapSnapshot(MapRenderSnapshot map)
    {
        ArgumentNullException.ThrowIfNull(map);
        LoadedMapRenderContext? context = _context;
        if (context is null) return;
        Point scroll = CurrentScroll;
        CancelPendingRenders();
        ClearEditPreview();
        ClearCache();
        _context = context with { Map = map };
        _pendingAnimationFrame = null;
        UpdateScrollExtent();
        AutoScrollPosition = scroll;
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SetContext(LoadedMapRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        CancelPendingRenders();
        ClearEditPreview();
        ClearCache();
        _context = context;
        _keyboardTile = Point.Empty;
        AccessibleDescription = $"Map canvas, {context.Map.Width} columns by {context.Map.Height} rows. Arrow keys move the cursor; Enter activates the current tile; Shift+F10 opens its context menu.";
        _animationFrame = 0;
        _pendingAnimationFrame = null;
        _animationTimer.Enabled = _animationEnabled && ContainsAnimatedTiles(context.Map);
        UpdateScrollExtent();
        AutoScrollPosition = Point.Empty;
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SetZoom(float zoom, Point? clientAnchor = null)
    {
        float clamped = Math.Clamp(zoom, 0.10f, 8f);
        if (Math.Abs(clamped - _zoom) < 0.0001f) return;
        Point anchor = clientAnchor ?? new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        Point scroll = CurrentScroll;
        double mapX = (scroll.X + anchor.X) / _zoom;
        double mapY = (scroll.Y + anchor.Y) / _zoom;
        _zoom = clamped;
        UpdateScrollExtent();
        int targetX = Math.Max(0, checked((int)Math.Round(mapX * _zoom - anchor.X)));
        int targetY = Math.Max(0, checked((int)Math.Round(mapY * _zoom - anchor.Y)));
        AutoScrollPosition = new Point(targetX, targetY);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void ZoomIn(Point? anchor = null) => SetZoom(NextZoom(increase: true), anchor);
    public void ZoomOut(Point? anchor = null) => SetZoom(NextZoom(increase: false), anchor);
    public void ActualSize() => SetZoom(1f);

    public void ZoomToFit()
    {
        if (_context is null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        PixelSize size = _context.Map.PixelSize;
        float horizontal = Math.Max(0.10f, (ClientSize.Width - 24) / (float)size.Width);
        float vertical = Math.Max(0.10f, (ClientSize.Height - 24) / (float)size.Height);
        SetZoom(Math.Min(4f, Math.Min(horizontal, vertical)), Point.Empty);
        AutoScrollPosition = Point.Empty;
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void CentreOnTile(int x, int y) => CentreOnMapPixel(
        checked(x * MvTileRules.TileWidth + MvTileRules.TileWidth / 2),
        checked(y * MvTileRules.TileHeight + MvTileRules.TileHeight / 2));

    public void CentreOnMapPixel(int pixelX, int pixelY)
    {
        if (_context is null) return;
        pixelX = Math.Clamp(pixelX, 0, Math.Max(0, _context.Map.PixelSize.Width - 1));
        pixelY = Math.Clamp(pixelY, 0, Math.Max(0, _context.Map.PixelSize.Height - 1));
        int targetX = Math.Max(0, checked((int)Math.Round(pixelX * _zoom - ClientSize.Width / 2d)));
        int targetY = Math.Max(0, checked((int)Math.Round(pixelY * _zoom - ClientSize.Height / 2d)));
        AutoScrollPosition = new Point(targetX, targetY);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        LoadedMapRenderContext? context = _context;
        if (context is null)
        {
            TextRenderer.DrawText(e.Graphics, "No map loaded", Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.CompositingMode = CompositingMode.SourceOver;
        Point scroll = CurrentScroll;
        PixelRect visibleMapPixels = VisibleMapPixelBounds(scroll);
        int chunkPixelSize = ChunkTiles * MvTileRules.TileWidth;
        int firstChunkX = Math.Max(0, visibleMapPixels.Left / chunkPixelSize);
        int firstChunkY = Math.Max(0, visibleMapPixels.Top / chunkPixelSize);
        int lastChunkX = Math.Min(DivideRoundUp(context.Map.PixelSize.Width, chunkPixelSize) - 1, Math.Max(firstChunkX, (Math.Max(0, visibleMapPixels.Right - 1)) / chunkPixelSize));
        int lastChunkY = Math.Min(DivideRoundUp(context.Map.PixelSize.Height, chunkPixelSize) - 1, Math.Max(firstChunkY, (Math.Max(0, visibleMapPixels.Bottom - 1)) / chunkPixelSize));

        for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
        {
            for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
            {
                ChunkKey key = new(chunkX, chunkY, _animationFrame, _renderGeneration);
                PixelRect chunkRegion = GetChunkRegion(context.Map, chunkX, chunkY);
                Rectangle destination = ScaleToClient(chunkRegion, scroll);
                if (_cache.TryGetValue(key, out CacheEntry? entry))
                {
                    entry.LastAccess = ++_accessCounter;
                    e.Graphics.DrawImage(entry.Bitmap, destination, new Rectangle(0, 0, entry.Bitmap.Width, entry.Bitmap.Height), GraphicsUnit.Pixel);
                }
                else
                {
                    // Never flash a checkerboard merely because the next animated
                    // frame is still rendering. Reuse the newest cached frame for
                    // this chunk until the requested frame is ready.
                    CacheEntry? fallback = FindFallbackChunk(chunkX, chunkY);
                    if (fallback is not null)
                    {
                        fallback.LastAccess = ++_accessCounter;
                        e.Graphics.DrawImage(fallback.Bitmap, destination, new Rectangle(0, 0, fallback.Bitmap.Width, fallback.Bitmap.Height), GraphicsUnit.Pixel);
                    }
                    else
                    {
                        DrawLoadingChunk(e.Graphics, destination);
                    }
                    QueueChunk(context, key, chunkRegion);
                }
            }
        }

        if (_previewBitmap is not null && !_previewRegion.IsEmpty)
        {
            Rectangle previewDestination = ScaleToClient(_previewRegion, scroll);
            e.Graphics.DrawImage(
                _previewBitmap,
                previewDestination,
                new Rectangle(0, 0, _previewBitmap.Width, _previewBitmap.Height),
                GraphicsUnit.Pixel);
        }

        DrawOverlays(e.Graphics, context, scroll, visibleMapPixels);
        DrawSelection(e.Graphics, scroll);
        DrawKeyboardCursor(e.Graphics, scroll);
    }


    protected override bool IsInputKey(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        return key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End
            || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        LoadedMapRenderContext? context = _context;
        if (context is null) return;
        Point previous = _keyboardTile;
        int step = e.Control ? 10 : 1;
        _keyboardTile = e.KeyCode switch
        {
            Keys.Left => new Point(Math.Max(0, _keyboardTile.X - step), _keyboardTile.Y),
            Keys.Right => new Point(Math.Min(context.Map.Width - 1, _keyboardTile.X + step), _keyboardTile.Y),
            Keys.Up => new Point(_keyboardTile.X, Math.Max(0, _keyboardTile.Y - step)),
            Keys.Down => new Point(_keyboardTile.X, Math.Min(context.Map.Height - 1, _keyboardTile.Y + step)),
            Keys.Home => new Point(0, e.Control ? 0 : _keyboardTile.Y),
            Keys.End => new Point(context.Map.Width - 1, e.Control ? context.Map.Height - 1 : _keyboardTile.Y),
            _ => _keyboardTile
        };
        if (_keyboardTile != previous)
        {
            CentreOnTile(_keyboardTile.X, _keyboardTile.Y);
            RaiseCoordinateChanged(_keyboardTile);
            AccessibleDescription = DescribeTile(context, _keyboardTile);
            AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
            e.Handled = e.SuppressKeyPress = true;
            Invalidate();
            return;
        }
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Point client = TileCentreClient(_keyboardTile);
            TilePointerDown?.Invoke(this, new MapTilePointerEventArgs(_keyboardTile.X, _keyboardTile.Y, MouseButtons.Left, e.Modifiers, client));
            TilePointerUp?.Invoke(this, new MapTilePointerEventArgs(_keyboardTile.X, _keyboardTile.Y, MouseButtons.Left, e.Modifiers, client));
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
        {
            Point client = TileCentreClient(_keyboardTile);
            TilePointerDown?.Invoke(this, new MapTilePointerEventArgs(_keyboardTile.X, _keyboardTile.Y, MouseButtons.Right, e.Modifiers, client));
            TilePointerUp?.Invoke(this, new MapTilePointerEventArgs(_keyboardTile.X, _keyboardTile.Y, MouseButtons.Right, e.Modifiers, client));
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Invalidate();
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (TryClientToMapTile(e.Location, out Point tile))
        {
            TileDoubleClicked?.Invoke(this, new MapTilePointerEventArgs(tile.X, tile.Y, e.Button, ModifierKeys, e.Location));
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) == Keys.Control)
        {
            if (e.Delta > 0) ZoomIn(e.Location); else if (e.Delta < 0) ZoomOut(e.Location);
            return;
        }
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Middle)
        {
            _middleDragging = true;
            _dragStart = e.Location;
            _dragScrollStart = CurrentScroll;
            Cursor = Cursors.Hand;
            Capture = true;
            return;
        }
        if (TryClientToMapTile(e.Location, out Point tile))
        {
            Capture = true;
            TilePointerDown?.Invoke(this, new MapTilePointerEventArgs(tile.X, tile.Y, e.Button, ModifierKeys, e.Location));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_middleDragging)
        {
            int x = Math.Max(0, _dragScrollStart.X - (e.X - _dragStart.X));
            int y = Math.Max(0, _dragScrollStart.Y - (e.Y - _dragStart.Y));
            AutoScrollPosition = new Point(x, y);
            ViewportChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }
        UpdateHover(e.Location);
        if (TryClientToMapTile(e.Location, out Point tile))
        {
            TilePointerMove?.Invoke(this, new MapTilePointerEventArgs(tile.X, tile.Y, Control.MouseButtons, ModifierKeys, e.Location));
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_middleDragging)
        {
            _middleDragging = false;
            Capture = false;
            Cursor = Cursors.Default;
            return;
        }
        if (TryClientToMapTile(e.Location, out Point tile))
        {
            TilePointerUp?.Invoke(this, new MapTilePointerEventArgs(tile.X, tile.Y, e.Button, ModifierKeys, e.Location));
        }
        Capture = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_middleDragging) return;
        _hoverTile = null;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        QueuePendingAnimationFrame();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        QueuePendingAnimationFrame();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _animationTimer.Stop();
            _animationTimer.Dispose();
            CancelPendingRenders();
            _renderCancellation.Dispose();
            _previewCancellation.Cancel();
            _previewCancellation.Dispose();
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            ClearCache();
        }
        base.Dispose(disposing);
    }

    private void QueueChunk(LoadedMapRenderContext context, ChunkKey key, PixelRect region)
    {
        if (!_pending.TryAdd(key, 0)) return;
        CancellationToken token = _renderCancellation.Token;
        _ = RenderChunkAsync(context, key, region, token);
    }

    private async Task RenderChunkAsync(LoadedMapRenderContext context, ChunkKey key, PixelRect region, CancellationToken cancellationToken)
    {
        Bitmap? bitmap = null;
        try
        {
            await _renderSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                MapRenderResult? result = await Task.Run(
                    () => RenderRegionWithoutCancellationEscape(
                        context.Map,
                        context.Tileset,
                        context.Assets,
                        region,
                        key.AnimationFrame,
                        cancellationToken),
                    CancellationToken.None).ConfigureAwait(false);
                if (result is null) return;
                bitmap = BitmapPixelConverter.ToBitmap(result.Image);
            }
            finally
            {
                _renderSemaphore.Release();
            }

            if (cancellationToken.IsCancellationRequested || IsDisposed || !ReferenceEquals(_context, context))
            {
                bitmap.Dispose();
                bitmap = null;
                return;
            }

            Bitmap completed = bitmap ?? throw new InvalidOperationException("The completed chunk bitmap was not created.");
            bitmap = null;
            if (IsHandleCreated && !IsDisposed)
            {
                try
                {
                    BeginInvoke(new Action(() => InstallChunk(context, key, completed)));
                }
                catch (InvalidOperationException)
                {
                    completed.Dispose();
                }
            }
            else
            {
                completed.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            bitmap?.Dispose();
        }
        catch (Exception)
        {
            bitmap?.Dispose();
            if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(Invalidate));
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    private void InstallChunk(LoadedMapRenderContext context, ChunkKey key, Bitmap bitmap)
    {
        if (IsDisposed || !ReferenceEquals(_context, context))
        {
            bitmap.Dispose();
            return;
        }
        if (_cache.Remove(key, out CacheEntry? replaced)) replaced.Bitmap.Dispose();
        _cache[key] = new CacheEntry(bitmap, ++_accessCounter);
        TrimCache();

        if (key.AnimationFrame == _animationFrame)
        {
            Invalidate(ScaleToClient(GetChunkRegion(context.Map, key.X, key.Y), CurrentScroll));
            return;
        }

        // Prefetched animation chunks are installed silently. The canvas changes
        // frames only after every currently visible chunk is ready, preventing
        // checkerboard flashes and mixed-frame tearing.
        if (_pendingAnimationFrame == key.AnimationFrame && AreVisibleChunksCached(context, key.AnimationFrame))
        {
            _animationFrame = key.AnimationFrame;
            _pendingAnimationFrame = null;
            Invalidate();
        }
    }

    private async Task RenderEditPreviewAsync(
        LoadedMapRenderContext baseContext,
        MapRenderSnapshot previewMap,
        PixelRect region,
        CancellationToken cancellationToken)
    {
        Bitmap? bitmap = null;
        try
        {
            MapRenderResult? result = await Task.Run(
                () => RenderRegionWithoutCancellationEscape(
                    previewMap,
                    baseContext.Tileset,
                    baseContext.Assets,
                    region,
                    _animationFrame,
                    cancellationToken),
                CancellationToken.None).ConfigureAwait(false);
            if (result is null || cancellationToken.IsCancellationRequested) return;
            bitmap = BitmapPixelConverter.ToBitmap(result.Image);
            if (cancellationToken.IsCancellationRequested) return;
            if (IsDisposed || !IsHandleCreated) return;
            Bitmap completed = bitmap ?? throw new InvalidOperationException("The edit preview bitmap was not created.");
            bitmap = null;
            BeginInvoke(new Action(() =>
            {
                if (cancellationToken.IsCancellationRequested || IsDisposed)
                {
                    completed.Dispose();
                    return;
                }
                _previewBitmap?.Dispose();
                _previewBitmap = completed;
                _previewRegion = region;
                Invalidate(ScaleToClient(region, CurrentScroll));
            }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(Invalidate));
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    /// <summary>
    /// Runs a renderer cancellation cooperatively without allowing the expected
    /// <see cref="OperationCanceledException"/> to cross the Task.Run boundary.
    /// Visual Studio otherwise reports routine preview replacement as a
    /// user-unhandled exception even though the outer async method catches it.
    /// </summary>
    private static MapRenderResult? RenderRegionWithoutCancellationEscape(
        MapRenderSnapshot map,
        TilesetRenderSnapshot tileset,
        MapRenderAssetSet assets,
        PixelRect region,
        int animationFrame,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return null;
        try
        {
            return new MapPixelRenderer().RenderRegion(
                map,
                tileset,
                assets,
                region,
                animationFrame,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private void DrawKeyboardCursor(Graphics graphics, Point scroll)
    {
        LoadedMapRenderContext? context = _context;
        if (!Focused || context is null || _keyboardTile.X < 0 || _keyboardTile.Y < 0
            || _keyboardTile.X >= context.Map.Width || _keyboardTile.Y >= context.Map.Height) return;
        Rectangle cell = ScaleToClient(new PixelRect(
            _keyboardTile.X * MvTileRules.TileWidth,
            _keyboardTile.Y * MvTileRules.TileHeight,
            MvTileRules.TileWidth,
            MvTileRules.TileHeight), scroll);
        using Pen outside = new(SystemInformation.HighContrast ? SystemColors.WindowText : Color.Black, 4f);
        using Pen inside = new(_focusColor, 2f);
        graphics.DrawRectangle(outside, cell);
        graphics.DrawRectangle(inside, Rectangle.Inflate(cell, -2, -2));
    }

    private Point TileCentreClient(Point tile)
    {
        Point scroll = CurrentScroll;
        int x = checked((int)Math.Round((tile.X * MvTileRules.TileWidth + MvTileRules.TileWidth / 2d) * _zoom - scroll.X));
        int y = checked((int)Math.Round((tile.Y * MvTileRules.TileHeight + MvTileRules.TileHeight / 2d) * _zoom - scroll.Y));
        return new Point(x, y);
    }

    private void RaiseCoordinateChanged(Point tile)
    {
        LoadedMapRenderContext? context = _context;
        if (context is null) return;
        int region = context.Map.ReadRegion(tile.X, tile.Y);
        int shadow = context.Map.ReadShadow(tile.X, tile.Y);
        int? eventId = context.Map.Events.FirstOrDefault(item => item.X == tile.X && item.Y == tile.Y)?.Id;
        CoordinateChanged?.Invoke(this, new MapCoordinateEventArgs(tile.X, tile.Y, region, shadow, eventId));
    }

    private static string DescribeTile(LoadedMapRenderContext context, Point tile)
    {
        int region = context.Map.ReadRegion(tile.X, tile.Y);
        MapEventRenderSnapshot? mapEvent = context.Map.Events.FirstOrDefault(item => item.X == tile.X && item.Y == tile.Y);
        string eventText = mapEvent is null ? "no event" : $"event {mapEvent.Id}, {mapEvent.Name}";
        return $"Map tile {tile.X}, {tile.Y}; region {region}; {eventText}. Arrow keys move; Enter activates; Shift+F10 opens the context action.";
    }

    private void DrawSelection(Graphics graphics, Point scroll)
    {
        if (_selectionRegion is not PixelRect selection || selection.IsEmpty) return;
        Rectangle rectangle = ScaleToClient(selection, scroll);
        using Pen outside = new(Color.Black, 3f) { DashStyle = DashStyle.Dash };
        using Pen inside = new(Color.White, 1f) { DashStyle = DashStyle.Dash };
        graphics.DrawRectangle(outside, rectangle);
        graphics.DrawRectangle(inside, rectangle);
    }

    private void DrawOverlays(Graphics graphics, LoadedMapRenderContext context, Point scroll, PixelRect visible)
    {
        int startX = Math.Max(0, visible.Left / MvTileRules.TileWidth);
        int startY = Math.Max(0, visible.Top / MvTileRules.TileHeight);
        int endX = Math.Min(context.Map.Width, DivideRoundUp(visible.Right, MvTileRules.TileWidth));
        int endY = Math.Min(context.Map.Height, DivideRoundUp(visible.Bottom, MvTileRules.TileHeight));

        using Pen gridPen = new(_gridColor, 1f);
        using Pen passabilityPen = new(_passabilityColor, Math.Max(1f, _zoom * 1.5f));
        using Pen shadowPen = new(_shadowColor, Math.Max(1f, _zoom));
        using SolidBrush labelBackground = new(_labelBackground);
        using SolidBrush labelForeground = new(Color.White);
        using Font labelFont = new(Font.FontFamily, Math.Max(7f, Math.Min(10f, Font.Size * Math.Max(0.75f, _zoom))));

        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                Rectangle cell = ScaleToClient(new PixelRect(x * MvTileRules.TileWidth, y * MvTileRules.TileHeight, MvTileRules.TileWidth, MvTileRules.TileHeight), scroll);
                if ((_overlays & MapOverlayOptions.Regions) != 0)
                {
                    int region = context.Map.ReadRegion(x, y);
                    if (region != 0)
                    {
                        using SolidBrush regionBrush = new(RegionColour(region));
                        graphics.FillRectangle(regionBrush, cell);
                        if (_zoom >= 0.45f) DrawLabel(graphics, region.ToString(System.Globalization.CultureInfo.InvariantCulture), cell, labelFont, labelBackground, labelForeground);
                    }
                }
                if ((_overlays & MapOverlayOptions.Shadows) != 0)
                {
                    int bits = context.Map.ReadShadow(x, y) & 0x0f;
                    int halfWidth = cell.Width / 2;
                    int halfHeight = cell.Height / 2;
                    for (int quarter = 0; quarter < 4; quarter++)
                    {
                        if ((bits & (1 << quarter)) == 0) continue;
                        Rectangle quarterRect = new(cell.X + quarter % 2 * halfWidth, cell.Y + quarter / 2 * halfHeight, Math.Max(1, halfWidth), Math.Max(1, halfHeight));
                        graphics.DrawRectangle(shadowPen, quarterRect);
                    }
                }
                if ((_overlays & MapOverlayOptions.Passability) != 0) DrawPassability(graphics, context, x, y, cell, passabilityPen);
                if ((_overlays & MapOverlayOptions.TerrainTags) != 0)
                {
                    int terrain = EffectiveTerrainTag(context, x, y);
                    if (terrain != 0) DrawLabel(graphics, $"T{terrain}", cell, labelFont, labelBackground, labelForeground);
                }
                if ((_overlays & MapOverlayOptions.Grid) != 0 && _zoom >= 0.35f) graphics.DrawRectangle(gridPen, cell);
            }
        }

        if ((_overlays & (MapOverlayOptions.EventIds | MapOverlayOptions.EventNames | MapOverlayOptions.EventPriorities)) != 0)
        {
            foreach (MapEventRenderSnapshot mapEvent in context.Map.Events.Where(item => item.X >= startX && item.X < endX && item.Y >= startY && item.Y < endY))
            {
                Rectangle cell = ScaleToClient(new PixelRect(mapEvent.X * MvTileRules.TileWidth, mapEvent.Y * MvTileRules.TileHeight, MvTileRules.TileWidth, MvTileRules.TileHeight), scroll);
                if ((_overlays & MapOverlayOptions.EventPriorities) != 0)
                {
                    Color priorityColour = mapEvent.PriorityType switch
                    {
                        <= 0 => Color.LimeGreen,
                        1 => Color.DeepSkyBlue,
                        _ => Color.MediumVioletRed
                    };
                    using Pen priorityPen = new(priorityColour, Math.Max(1f, _zoom * 2f));
                    graphics.DrawRectangle(priorityPen, cell);
                }
                if ((_overlays & (MapOverlayOptions.EventIds | MapOverlayOptions.EventNames)) != 0)
                {
                    string text = (_overlays & MapOverlayOptions.EventNames) != 0 && !string.IsNullOrWhiteSpace(mapEvent.Name)
                        ? $"{mapEvent.Id}: {mapEvent.Name}"
                        : mapEvent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    DrawLabel(graphics, text, cell, labelFont, labelBackground, labelForeground);
                }
            }
        }

        if (_hoverTile is Point hover)
        {
            Rectangle cell = ScaleToClient(new PixelRect(hover.X * MvTileRules.TileWidth, hover.Y * MvTileRules.TileHeight, MvTileRules.TileWidth, MvTileRules.TileHeight), scroll);
            using Pen hoverPen = new(Color.DeepSkyBlue, 2f);
            graphics.DrawRectangle(hoverPen, cell);
        }
    }

    private static int EffectiveTerrainTag(LoadedMapRenderContext context, int x, int y)
    {
        for (int layer = 3; layer >= 0; layer--)
        {
            int tileId = context.Map.Read(x, y, layer);
            if (tileId == 0) continue;
            TilesetFlags flags = context.Tileset.GetFlags(tileId);
            if (flags.IsStar && flags.TerrainTag == 0) continue;
            return flags.TerrainTag;
        }
        return 0;
    }

    private static void DrawPassability(Graphics graphics, LoadedMapRenderContext context, int x, int y, Rectangle cell, Pen pen)
    {
        PassageDirection blocked = PassageDirection.None;
        foreach ((PassageDirection direction, int bit) in new[]
                 {
                     (PassageDirection.Down, 0x01),
                     (PassageDirection.Left, 0x02),
                     (PassageDirection.Right, 0x04),
                     (PassageDirection.Up, 0x08)
                 })
        {
            bool? passable = null;
            for (int layer = 3; layer >= 0; layer--)
            {
                int tileId = context.Map.Read(x, y, layer);
                TilesetFlags flags = context.Tileset.GetFlags(tileId);
                if (flags.IsStar) continue;
                passable = (flags.RawValue & bit) == 0;
                break;
            }
            if (passable == false) blocked |= direction;
        }
        if ((blocked & PassageDirection.Down) != 0) graphics.DrawLine(pen, cell.Left, cell.Bottom - 1, cell.Right, cell.Bottom - 1);
        if ((blocked & PassageDirection.Left) != 0) graphics.DrawLine(pen, cell.Left, cell.Top, cell.Left, cell.Bottom);
        if ((blocked & PassageDirection.Right) != 0) graphics.DrawLine(pen, cell.Right - 1, cell.Top, cell.Right - 1, cell.Bottom);
        if ((blocked & PassageDirection.Up) != 0) graphics.DrawLine(pen, cell.Left, cell.Top, cell.Right, cell.Top);
    }

    private void UpdateHover(Point clientPoint)
    {
        LoadedMapRenderContext? context = _context;
        if (context is null) return;
        Point scroll = CurrentScroll;
        int mapPixelX = checked((int)Math.Floor((scroll.X + clientPoint.X) / _zoom));
        int mapPixelY = checked((int)Math.Floor((scroll.Y + clientPoint.Y) / _zoom));
        int x = mapPixelX / MvTileRules.TileWidth;
        int y = mapPixelY / MvTileRules.TileHeight;
        Point? next = x >= 0 && y >= 0 && x < context.Map.Width && y < context.Map.Height ? new Point(x, y) : null;
        if (_hoverTile == next) return;
        _hoverTile = next;
        if (next is Point tile)
        {
            int? eventId = context.Map.Events.FirstOrDefault(item => item.X == tile.X && item.Y == tile.Y)?.Id;
            CoordinateChanged?.Invoke(this, new MapCoordinateEventArgs(tile.X, tile.Y, context.Map.ReadRegion(tile.X, tile.Y), context.Map.ReadShadow(tile.X, tile.Y) & 0x0f, eventId));
        }
        Invalidate();
    }

    private void AdvanceAnimation()
    {
        LoadedMapRenderContext? context = _context;
        if (context is null || !_animationEnabled) return;

        int nextFrame = (_animationFrame + 1) % 12;
        if (AreVisibleChunksCached(context, nextFrame))
        {
            _animationFrame = nextFrame;
            _pendingAnimationFrame = null;
            Invalidate();
            return;
        }

        _pendingAnimationFrame = nextFrame;
        QueueVisibleFrame(context, nextFrame);
    }

    private void QueuePendingAnimationFrame()
    {
        if (_context is not { } context || _pendingAnimationFrame is not int frame) return;
        QueueVisibleFrame(context, frame);
    }

    private void QueueVisibleFrame(LoadedMapRenderContext context, int frame)
    {
        foreach ((ChunkKey key, PixelRect region) in EnumerateVisibleChunks(context, frame))
        {
            if (!_cache.ContainsKey(key)) QueueChunk(context, key, region);
        }
    }

    private bool AreVisibleChunksCached(LoadedMapRenderContext context, int frame)
    {
        bool foundAny = false;
        foreach ((ChunkKey key, _) in EnumerateVisibleChunks(context, frame))
        {
            foundAny = true;
            if (!_cache.ContainsKey(key)) return false;
        }
        return foundAny;
    }

    private IEnumerable<(ChunkKey Key, PixelRect Region)> EnumerateVisibleChunks(LoadedMapRenderContext context, int frame)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) yield break;

        PixelRect visible = VisibleMapPixelBounds(CurrentScroll);
        int chunkPixelSize = ChunkTiles * MvTileRules.TileWidth;
        int firstChunkX = Math.Max(0, visible.Left / chunkPixelSize);
        int firstChunkY = Math.Max(0, visible.Top / chunkPixelSize);
        int lastChunkX = Math.Min(
            DivideRoundUp(context.Map.PixelSize.Width, chunkPixelSize) - 1,
            Math.Max(firstChunkX, Math.Max(0, visible.Right - 1) / chunkPixelSize));
        int lastChunkY = Math.Min(
            DivideRoundUp(context.Map.PixelSize.Height, chunkPixelSize) - 1,
            Math.Max(firstChunkY, Math.Max(0, visible.Bottom - 1) / chunkPixelSize));

        for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
        {
            for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
            {
                yield return (
                    new ChunkKey(chunkX, chunkY, frame, _renderGeneration),
                    GetChunkRegion(context.Map, chunkX, chunkY));
            }
        }
    }

    private CacheEntry? FindFallbackChunk(int chunkX, int chunkY)
    {
        CacheEntry? newest = null;
        long newestAccess = long.MinValue;
        foreach ((ChunkKey key, CacheEntry entry) in _cache)
        {
            if (key.X != chunkX || key.Y != chunkY || key.Generation != _renderGeneration) continue;
            if (entry.LastAccess <= newestAccess) continue;
            newest = entry;
            newestAccess = entry.LastAccess;
        }
        return newest;
    }

    private void UpdateScrollExtent()
    {
        if (_context is null)
        {
            AutoScrollMinSize = Size.Empty;
            return;
        }
        PixelSize size = _context.Map.PixelSize;
        AutoScrollMinSize = new Size(
            Math.Max(1, checked((int)Math.Ceiling(size.Width * _zoom))),
            Math.Max(1, checked((int)Math.Ceiling(size.Height * _zoom))));
    }

    private PixelRect VisibleMapPixelBounds(Point scroll)
    {
        int left = Math.Max(0, checked((int)Math.Floor(scroll.X / _zoom)));
        int top = Math.Max(0, checked((int)Math.Floor(scroll.Y / _zoom)));
        int right = checked((int)Math.Ceiling((scroll.X + ClientSize.Width) / _zoom));
        int bottom = checked((int)Math.Ceiling((scroll.Y + ClientSize.Height) / _zoom));
        return new PixelRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private Rectangle ScaleToClient(PixelRect rectangle, Point scroll) => new(
        checked((int)Math.Floor(rectangle.X * _zoom - scroll.X)),
        checked((int)Math.Floor(rectangle.Y * _zoom - scroll.Y)),
        Math.Max(1, checked((int)Math.Ceiling(rectangle.Width * _zoom))),
        Math.Max(1, checked((int)Math.Ceiling(rectangle.Height * _zoom))));

    private static PixelRect GetChunkRegion(MapRenderSnapshot map, int chunkX, int chunkY)
    {
        int size = ChunkTiles * MvTileRules.TileWidth;
        int x = chunkX * size;
        int y = chunkY * size;
        return new PixelRect(x, y, Math.Min(size, map.PixelSize.Width - x), Math.Min(size, map.PixelSize.Height - y));
    }

    private void TrimCache()
    {
        while (_cache.Count > MaximumCachedChunks)
        {
            KeyValuePair<ChunkKey, CacheEntry> oldest = _cache.OrderBy(static pair => pair.Value.LastAccess).First();
            _cache.Remove(oldest.Key);
            oldest.Value.Bitmap.Dispose();
        }
    }

    private void ClearCache()
    {
        foreach (CacheEntry entry in _cache.Values) entry.Bitmap.Dispose();
        _cache.Clear();
    }

    private void CancelPendingRenders()
    {
        _renderCancellation.Cancel();
        _renderCancellation.Dispose();
        _renderCancellation = new CancellationTokenSource();
        unchecked { _renderGeneration++; }
        _pending.Clear();
    }

    private float NextZoom(bool increase)
    {
        float[] levels = [0.10f, 0.125f, 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f, 3f, 4f, 6f, 8f];
        return increase
            ? levels.FirstOrDefault(level => level > _zoom + 0.001f, levels[^1])
            : levels.LastOrDefault(level => level < _zoom - 0.001f, levels[0]);
    }

    private Point CurrentScroll => new(-AutoScrollPosition.X, -AutoScrollPosition.Y);

    private static bool ContainsAnimatedTiles(MapRenderSnapshot map)
    {
        foreach (int tileId in map.Data.Span)
        {
            if (MvTileRules.IsTileA1(tileId)) return true;
        }
        return map.Events.Any(item => MvTileRules.IsTileA1(item.Graphic.TileId));
    }

    private void DrawLoadingChunk(Graphics graphics, Rectangle destination)
    {
        using SolidBrush first = new(_checkerboardLight);
        using SolidBrush second = new(_checkerboardDark);
        const int cell = 16;
        graphics.FillRectangle(first, destination);
        for (int y = destination.Top; y < destination.Bottom; y += cell)
        {
            for (int x = destination.Left; x < destination.Right; x += cell)
            {
                if ((((x - destination.Left) / cell) + ((y - destination.Top) / cell)) % 2 == 0) continue;
                graphics.FillRectangle(second, x, y, Math.Min(cell, destination.Right - x), Math.Min(cell, destination.Bottom - y));
            }
        }
    }

    private static void DrawLabel(Graphics graphics, string text, Rectangle cell, Font font, Brush background, Brush foreground)
    {
        Size size = TextRenderer.MeasureText(text, font, new Size(Math.Max(1, cell.Width * 4), Math.Max(1, cell.Height)), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        Rectangle label = new(cell.Left + 2, cell.Top + 2, Math.Min(size.Width + 6, Math.Max(8, cell.Width * 4)), size.Height + 3);
        graphics.FillRectangle(background, label);
        TextRenderer.DrawText(graphics, text, font, label, ((SolidBrush)foreground).Color, TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static Color RegionColour(int region)
    {
        int r = 64 + (region * 73) % 160;
        int g = 64 + (region * 151) % 160;
        int b = 64 + (region * 199) % 160;
        return Color.FromArgb(86, r, g, b);
    }

    private static int DivideRoundUp(int value, int divisor) => checked((value + divisor - 1) / divisor);

    private readonly record struct ChunkKey(int X, int Y, int AnimationFrame, int Generation);
    private sealed class CacheEntry
    {
        public CacheEntry(Bitmap bitmap, long lastAccess)
        {
            Bitmap = bitmap;
            LastAccess = lastAccess;
        }
        public Bitmap Bitmap { get; }
        public long LastAccess { get; set; }
    }
}
