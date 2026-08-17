using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.Tilemaps;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class TilesetPaletteControl : ScrollableControl
{
    private readonly SpriteEditorSession session;
    private int selectedTileIndex;
    public TilesetPaletteControl(SpriteEditorSession session) { this.session = session; DoubleBuffered = true; AutoScroll = true; AutoScrollOffset = Point.Empty; BackColor = SystemColors.ControlDarkDark; }
    public Guid? TilesetId { get; set; }
    public int SelectedTileIndex { get => selectedTileIndex; set { selectedTileIndex = value; Invalidate(); SelectedTileChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? SelectedTileChanged;
    public int CellSize { get; set; } = 56;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (TilesetId is not Guid id) return; SpriteTileset? tileset = session.Document.Tilesets.FirstOrDefault(item => item.Id == id); if (tileset is null) return;
        int columns = Math.Max(1, ClientSize.Width / CellSize); int rows = (int)Math.Ceiling(tileset.Tiles.Count / (double)columns); AutoScrollMinSize = new Size(columns * CellSize, rows * CellSize);
        for (int index = 0; index < tileset.Tiles.Count; index++)
        {
            SpriteTileDefinition tile = tileset.Tiles[index]; int column = index % columns, row = index / columns; Rectangle cell = new(column * CellSize + AutoScrollPosition.X, row * CellSize + AutoScrollPosition.Y, CellSize, CellSize);
            e.Graphics.FillRectangle(Brushes.DimGray, cell);
            if (!tile.Empty && tile.PixelContentId is Guid contentId && session.Package.PixelBuffers.TryGetValue(contentId, out PixelBuffer? pixels))
            {
                using Bitmap bitmap = PixelBufferBitmap.ToBitmap(pixels); e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor; e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                e.Graphics.DrawImage(bitmap, new Rectangle(cell.X + 4, cell.Y + 4, CellSize - 8, CellSize - 8));
            }
            e.Graphics.DrawRectangle(tile.Index == SelectedTileIndex ? Pens.Yellow : Pens.Black, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
            TextRenderer.DrawText(e.Graphics, tile.Index.ToString(), Font, new Rectangle(cell.X + 2, cell.Bottom - 18, cell.Width - 4, 16), Color.White, TextFormatFlags.Right);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (TilesetId is not Guid id) return; SpriteTileset? tileset = session.Document.Tilesets.FirstOrDefault(item => item.Id == id); if (tileset is null) return;
        int columns = Math.Max(1, ClientSize.Width / CellSize); int x = e.X - AutoScrollPosition.X, y = e.Y - AutoScrollPosition.Y; int index = y / CellSize * columns + x / CellSize;
        if (index >= 0 && index < tileset.Tiles.Count) SelectedTileIndex = tileset.Tiles[index].Index;
    }
}

internal sealed class TilemapGridControl : ScrollableControl
{
    private readonly SpriteEditorSession session;
    private SpritePackageData? beforeStroke;
    private Point? lastCell;
    private Point selectionStart;
    private Rectangle selectedCells;
    public TilemapGridControl(SpriteEditorSession session) { this.session = session; DoubleBuffered = true; AutoScroll = true; BackColor = Color.FromArgb(42, 42, 46); SetStyle(ControlStyles.ResizeRedraw, true); }
    public int SelectedTileIndex { get; set; }
    public IReadOnlyList<int> StackTileIndexes { get; set; } = [];
    public SpriteTileTransform Transform { get; set; }
    public SpriteTileEditMode EditMode { get; set; } = SpriteTileEditMode.Manual;
    public int Zoom { get; set; } = 2;
    public Rectangle SelectedCells => selectedCells;
    public event EventHandler? SelectionChanged;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); SpriteLayer? layer = SelectedTilemapLayer(); if (layer is null) { DrawCentered(e.Graphics, "Select or create a tilemap layer."); return; }
        SpriteTilemapCel? cel = layer.TilemapCels.FirstOrDefault(item => item.FrameId == session.ActiveFrameId); if (cel is null) { DrawCentered(e.Graphics, "Create a tilemap cel for this frame."); return; }
        SpriteTileset tileset = session.Document.Tilesets.First(item => item.Id == layer.TilesetId); SpriteTilemapContent map = session.Document.TilemapContents.First(item => item.Id == cel.TilemapContentId);
        PixelBuffer rendered = TilemapRasterizer.Render(session.Document, session.Package.PixelBuffers, layer, cel); using Bitmap bitmap = PixelBufferBitmap.ToBitmap(rendered);
        int scale = Math.Max(1, Zoom); int offsetX = AutoScrollPosition.X, offsetY = AutoScrollPosition.Y;
        AutoScrollMinSize = new Size(rendered.Width * scale, rendered.Height * scale);
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor; e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        e.Graphics.DrawImage(bitmap, new Rectangle(offsetX, offsetY, rendered.Width * scale, rendered.Height * scale));
        using Pen grid = new(Color.FromArgb(120, Color.White)); for (int x = 0; x <= map.Columns; x++) e.Graphics.DrawLine(grid, offsetX + x * tileset.TileWidth * scale, offsetY, offsetX + x * tileset.TileWidth * scale, offsetY + map.Rows * tileset.TileHeight * scale);
        for (int y = 0; y <= map.Rows; y++) e.Graphics.DrawLine(grid, offsetX, offsetY + y * tileset.TileHeight * scale, offsetX + map.Columns * tileset.TileWidth * scale, offsetY + y * tileset.TileHeight * scale);
        if (selectedCells.Width > 0 && selectedCells.Height > 0)
        {
            Rectangle rectangle = new(offsetX + selectedCells.X * tileset.TileWidth * scale, offsetY + selectedCells.Y * tileset.TileHeight * scale, selectedCells.Width * tileset.TileWidth * scale, selectedCells.Height * tileset.TileHeight * scale);
            using Pen selection = new(Color.Yellow, 2); e.Graphics.DrawRectangle(selection, rectangle);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus(); SpriteLayer? layer = SelectedTilemapLayer(); if (layer is null || layer.Locked || EditMode == SpriteTileEditMode.Pixel) return;
        if (layer.TilemapCels.All(item => item.FrameId != session.ActiveFrameId)) session.EnsureSelectedTilemapCel(layer.Continuous);
        Point? cell = HitCell(e.Location, layer); if (cell is null) return;
        if ((ModifierKeys & Keys.Shift) != 0)
        {
            selectionStart = cell.Value; selectedCells = new Rectangle(cell.Value.X, cell.Value.Y, 1, 1); Capture = true; Invalidate(); return;
        }
        beforeStroke = session.CapturePackageSnapshot(); lastCell = null; Capture = true; ApplyCell(layer, cell.Value, e.Button == MouseButtons.Right);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (!Capture) return; SpriteLayer? layer = SelectedTilemapLayer(); if (layer is null) return; Point? cell = HitCell(e.Location, layer); if (cell is null) return;
        if ((ModifierKeys & Keys.Shift) != 0 && beforeStroke is null)
        {
            int left = Math.Min(selectionStart.X, cell.Value.X), top = Math.Min(selectionStart.Y, cell.Value.Y); selectedCells = new Rectangle(left, top, Math.Abs(selectionStart.X - cell.Value.X) + 1, Math.Abs(selectionStart.Y - cell.Value.Y) + 1); Invalidate(); return;
        }
        if (beforeStroke is not null) ApplyCell(layer, cell.Value, e.Button == MouseButtons.Right || (Control.MouseButtons & MouseButtons.Right) != 0);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); Capture = false; lastCell = null;
        if (beforeStroke is not null) { SpritePackageData before = beforeStroke; beforeStroke = null; session.CommitTilemapEdit("Paint Tilemap", before); }
        else SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void FillSelection(TileBrush brush)
    {
        SpriteLayer layer = SelectedTilemapLayer() ?? throw new InvalidOperationException("Select a tilemap layer first."); SpritePackageData before = session.CapturePackageSnapshot(); Rectangle region = selectedCells.Width > 0 ? selectedCells : new Rectangle(0, 0, 1, 1);
        TilemapDocumentOperations.FillRectangle(session.Document, layer, session.ActiveFrameId, new IntRect(region.X, region.Y, region.Width, region.Height), brush); session.CommitTilemapEdit("Fill Tilemap Region", before);
    }

    public void FloodFill(TileBrush brush)
    {
        SpriteLayer layer = SelectedTilemapLayer() ?? throw new InvalidOperationException("Select a tilemap layer first."); Rectangle region = selectedCells.Width > 0 ? selectedCells : new Rectangle(0, 0, 1, 1); SpritePackageData before = session.CapturePackageSnapshot();
        TilemapDocumentOperations.Fill(session.Document, layer, session.ActiveFrameId, region.X, region.Y, brush); session.CommitTilemapEdit("Flood Fill Tilemap", before);
    }

    public void TransformSelection(SpriteTileTransform transform)
    {
        SpriteLayer layer = SelectedTilemapLayer() ?? throw new InvalidOperationException("Select a tilemap layer first."); Rectangle region = selectedCells.Width > 0 ? selectedCells : new Rectangle(0, 0, 1, 1); SpritePackageData before = session.CapturePackageSnapshot();
        TilemapDocumentOperations.TransformRegion(session.Document, layer, session.ActiveFrameId, new IntRect(region.X, region.Y, region.Width, region.Height), transform); session.CommitTilemapEdit("Transform Tilemap Region", before);
    }

    public void ClearSelection()
    {
        SpriteLayer layer = SelectedTilemapLayer() ?? throw new InvalidOperationException("Select a tilemap layer first."); Rectangle region = selectedCells.Width > 0 ? selectedCells : new Rectangle(0, 0, 1, 1); SpritePackageData before = session.CapturePackageSnapshot();
        TilemapDocumentOperations.FillRectangle(session.Document, layer, session.ActiveFrameId, new IntRect(region.X, region.Y, region.Width, region.Height), new TileBrush([-1])); session.CommitTilemapEdit("Clear Tilemap Region", before);
    }

    private void ApplyCell(SpriteLayer layer, Point cell, bool erase)
    {
        if (lastCell == cell) return; lastCell = cell;
        if (erase) TilemapDocumentOperations.Erase(session.Document, layer, session.ActiveFrameId, cell.X, cell.Y);
        else TilemapDocumentOperations.Paint(session.Document, layer, session.ActiveFrameId, cell.X, cell.Y, new TileBrush(StackTileIndexes.Count > 0 ? StackTileIndexes : [SelectedTileIndex], Transform), EditMode);
        Invalidate();
    }

    private Point? HitCell(Point location, SpriteLayer layer)
    {
        SpriteTileset tileset = session.Document.Tilesets.First(item => item.Id == layer.TilesetId); SpriteTilemapCel? cel = layer.TilemapCels.FirstOrDefault(item => item.FrameId == session.ActiveFrameId); if (cel is null) return null;
        SpriteTilemapContent map = session.Document.TilemapContents.First(item => item.Id == cel.TilemapContentId); int x = (location.X - AutoScrollPosition.X) / Math.Max(1, tileset.TileWidth * Zoom), y = (location.Y - AutoScrollPosition.Y) / Math.Max(1, tileset.TileHeight * Zoom); return x >= 0 && x < map.Columns && y >= 0 && y < map.Rows ? new Point(x, y) : null;
    }
    private SpriteLayer? SelectedTilemapLayer() => session.SelectedLayer.Kind == SpriteLayerKind.Tilemap ? session.SelectedLayer : null;
    private void DrawCentered(Graphics graphics, string text) => TextRenderer.DrawText(graphics, text, Font, ClientRectangle, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
}

internal sealed class TilePixelEditorControl : Control
{
    private readonly SpriteEditorSession session; private PixelBuffer? before; private Guid? contentId;
    public TilePixelEditorControl(SpriteEditorSession session) { this.session = session; DoubleBuffered = true; BackColor = Color.FromArgb(45,45,48); }
    public Guid? TilesetId { get; set; }
    public int TileIndex { get; set; }
    public Rgba32 ForegroundColor { get; set; } = Rgba32.Black;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (!TryTile(out _, out PixelBuffer? pixels)) return; int scale = Math.Max(1, Math.Min(ClientSize.Width / pixels.Width, ClientSize.Height / pixels.Height)); using Bitmap bitmap = PixelBufferBitmap.ToBitmap(pixels);
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor; e.Graphics.DrawImage(bitmap, new Rectangle(0, 0, pixels.Width * scale, pixels.Height * scale)); using Pen grid = new(Color.FromArgb(75, Color.White));
        for (int x=0;x<=pixels.Width;x++) e.Graphics.DrawLine(grid,x*scale,0,x*scale,pixels.Height*scale); for(int y=0;y<=pixels.Height;y++) e.Graphics.DrawLine(grid,0,y*scale,pixels.Width*scale,y*scale);
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (!TryTile(out Guid id, out PixelBuffer? pixels)) return; before = pixels.Clone(); contentId = id; Capture = true; PaintAt(e, pixels); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture && contentId is not null && session.Package.PixelBuffers.TryGetValue(contentId.Value, out PixelBuffer? pixels)) PaintAt(e, pixels); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; if (before is not null && contentId is Guid id) session.CommitPixelContentEdit("Edit Tile Pixels", id, before); before = null; contentId = null; Invalidate(); }
    private void PaintAt(MouseEventArgs e, PixelBuffer pixels) { int scale = Math.Max(1, Math.Min(ClientSize.Width / pixels.Width, ClientSize.Height / pixels.Height)); int x=e.X/scale,y=e.Y/scale; if(x>=0&&x<pixels.Width&&y>=0&&y<pixels.Height){pixels.SetPixel(x,y,e.Button==MouseButtons.Right?Rgba32.Transparent:ForegroundColor);Invalidate();} }
    private bool TryTile(out Guid id, out PixelBuffer? pixels) { id=Guid.Empty; pixels=null; if(TilesetId is not Guid setId)return false; SpriteTileset? set=session.Document.Tilesets.FirstOrDefault(item=>item.Id==setId); SpriteTileDefinition? tile=set?.Tiles.FirstOrDefault(item=>item.Index==TileIndex); if(tile?.PixelContentId is not Guid content)return false; id=content; return session.Package.PixelBuffers.TryGetValue(content,out pixels); }
}
