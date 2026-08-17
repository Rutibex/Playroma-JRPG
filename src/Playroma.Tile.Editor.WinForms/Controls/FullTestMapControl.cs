using System.Drawing.Drawing2D;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Tilesets.Models;
using Playroma.Tile.Tilesets.Rendering;

namespace Playroma.Tile.Editor.WinForms.Controls;

public sealed class FullTestMapControl : Control
{
    private readonly FullTestMapRenderer _renderer = new();
    private IReadOnlyDictionary<MvSheetSlot, Bitmap> _sheets = new Dictionary<MvSheetSlot, Bitmap>();
    private TilesetAssemblyDefinition? _definition;
    private TestMapDefinition? _map;
    private double _zoom = 0.75;

    public FullTestMapControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(38, 38, 40);
        TabStop = true;
    }

    public event EventHandler? MapChanged;
    public event EventHandler<string>? StatusChanged;
    public int SelectedTileAddress { get; set; } = -1;
    public TestMapLayer ActiveLayer { get; set; } = TestMapLayer.Lower1;
    public bool ShowGrid { get; set; } = true;
    public bool ShowCollision { get; set; }
    public bool PlayerMode { get; set; }

    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, 0.25, 3); UpdateSize(); Invalidate(); }
    }

    public void Bind(TestMapDefinition map, TilesetAssemblyDefinition definition, IReadOnlyDictionary<MvSheetSlot, Bitmap> sheets)
    {
        _map = map;
        _definition = definition;
        _sheets = sheets;
        UpdateSize();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (_map is null || _definition is null) return;
        using var image = _renderer.Render(_map, _sheets, _definition, ShowGrid, true, ShowCollision);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(image, new Rectangle(0, 0, (int)(image.Width * Zoom), (int)(image.Height * Zoom)));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_map is null) return;
        var x = (int)(e.X / Zoom) / 48;
        var y = (int)(e.Y / Zoom) / 48;
        if (x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return;
        if (PlayerMode)
        {
            _map.PlayerX = x;
            _map.PlayerY = y;
        }
        else
        {
            _map.Set(ActiveLayer, x, y, e.Button == MouseButtons.Right ? -1 : SelectedTileAddress);
        }
        MapChanged?.Invoke(this, EventArgs.Empty);
        StatusChanged?.Invoke(this, $"Map {x},{y} — {ActiveLayer}");
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_map is null || _definition is null) return;
        var moved = e.KeyCode switch
        {
            Keys.Left => _renderer.TryMovePlayer(_map, _definition, -1, 0),
            Keys.Right => _renderer.TryMovePlayer(_map, _definition, 1, 0),
            Keys.Up => _renderer.TryMovePlayer(_map, _definition, 0, -1),
            Keys.Down => _renderer.TryMovePlayer(_map, _definition, 0, 1),
            _ => false
        };
        if (!moved) return;
        e.Handled = true;
        MapChanged?.Invoke(this, EventArgs.Empty);
        var damage = _renderer.IsDamageFloor(_map, _definition, _map.PlayerX, _map.PlayerY);
        StatusChanged?.Invoke(this, damage ? "Player moved onto a damage floor." : "Player moved.");
        Invalidate();
    }

    private void UpdateSize()
    {
        if (_map is null) return;
        Size = new Size((int)(_map.Width * 48 * Zoom), (int)(_map.Height * 48 * Zoom));
    }
}
